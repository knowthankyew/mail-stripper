using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MailStripper.Models;
using MimeKit;

namespace MailStripper.Services;

public partial class UnsubscribeForensicAnalyzer : IUnsubscribeForensicAnalyzer
{
    private const int MaxForwardedDepth = 3;
    private const int MaxUnsubscribeCandidates = 50;
    private const int MaxHtmlScanLength = 512_000;
    private const string MandatoryReconWarning =
        "Executing an unsubscribe request contacts the remote host and can confirm to the sender that your mailbox is active. Only proceed for senders or ESPs you recognize.";

    private static readonly HashSet<string> KnownMultiPartPublicSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "gov.uk", "ac.uk", "me.uk", "net.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.nz", "org.nz", "net.nz", "govt.nz",
        "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp",
        "com.br", "org.br", "net.br", "gov.br",
        "com.mx", "org.mx", "gob.mx",
        "co.in", "net.in", "org.in", "gov.in", "ac.in",
        "com.sg", "org.sg", "edu.sg", "gov.sg",
        "co.za", "org.za", "net.za", "gov.za",
        "com.hk", "org.hk", "edu.hk", "gov.hk",
        "com.tw", "org.tw", "edu.tw", "gov.tw",
        "co.kr", "or.kr", "go.kr", "ac.kr",
        "co.il", "org.il", "ac.il", "gov.il",
        "com.ar", "com.co", "com.tr", "com.my", "com.ph", "com.vn", "com.ua", "com.pl"
    };

    private static readonly HashSet<string> KnownEspDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "sendgrid.net",
        "list-manage.com",
        "rsgsv.net",
        "mcsv.net",
        "amazonses.com",
        "awstrack.me",
        "pstmrk.it",
        "mtasv.net",
        "hubspot.com",
        "hubspotemail.net",
        "hs-sites.com",
        "klaviyomail.com",
        "klaviyo.com",
        "constantcontact.com",
        "rs6.net",
        "substack.com",
        "mailgun.org",
        "mailgun.net",
        "sparkpostmail.com",
        "sailthru.com",
        "braze.com",
        "customer.io",
        "customeriomail.com",
        "brevo.com",
        "sendinblue.com",
        "convertkit.com",
        "ck.page",
        "beehiiv.com"
    };

    private static readonly HashSet<string> UrlShortenerDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly", "tinyurl.com", "t.co", "goo.gl", "ow.ly",
        "is.gd", "buff.ly", "rebrand.ly", "rb.gy", "cutt.ly",
        "shorturl.at", "tiny.cc", "bl.ink"
    };

    private static readonly string[] UnsubscribeKeywords =
    [
        "unsubscribe",
        "opt-out",
        "opt out",
        "optout",
        "email preferences",
        "manage preferences",
        "notification preferences",
        "subscription preferences",
        "manage subscriptions",
        "remove me",
        "stop receiving"
    ];

    public UnsubscribeForensicReport Analyze(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var targetMessage = ResolveTargetMessage(message, 0);
        return AnalyzeSingleMessage(targetMessage);
    }

    private static MimeMessage ResolveTargetMessage(MimeMessage current, int depth)
    {
        if (depth >= MaxForwardedDepth)
        {
            return current;
        }

        bool hasUnsubHeaders = current.Headers.Contains(HeaderId.ListUnsubscribe) ||
                               current.Headers.Contains(HeaderId.ListUnsubscribePost);
        if (hasUnsubHeaders)
        {
            return current;
        }

        var html = current.HtmlBody ?? string.Empty;
        var text = current.TextBody ?? string.Empty;
        bool bodyMentionsUnsub = ContainsUnsubscribeKeyword(html) || ContainsUnsubscribeKeyword(text);
        if (bodyMentionsUnsub)
        {
            return current;
        }

        foreach (var part in current.BodyParts)
        {
            if (part is MessagePart msgPart && msgPart.Message != null)
            {
                return ResolveTargetMessage(msgPart.Message, depth + 1);
            }
        }

        return current;
    }

    private static UnsubscribeForensicReport AnalyzeSingleMessage(MimeMessage message)
    {
        var report = new UnsubscribeForensicReport();

        // 1. Detect synthetic pasted snippet (clipboard@local from ParseTextAsync)
        bool isSyntheticPaste = message.From.Mailboxes.Any(m =>
            string.Equals(m.Address, "clipboard@local", StringComparison.OrdinalIgnoreCase));

        if (isSyntheticPaste)
        {
            report.HeaderContextAvailable = false;
            report.Provenance = AuthenticationProvenance.SyntheticPastedBody;
        }

        // 2. Collect sender organizational domains (From + Return-Path)
        var senderDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mb in message.From.Mailboxes)
        {
            var dom = ExtractRegistrableDomainFromEmail(mb.Address);
            if (!string.IsNullOrEmpty(dom))
            {
                senderDomains.Add(dom);
            }
        }

        var returnPathHeader = message.Headers[HeaderId.ReturnPath];
        if (!string.IsNullOrWhiteSpace(returnPathHeader))
        {
            var rpClean = returnPathHeader.Trim().Trim('<', '>');
            var rpDom = ExtractRegistrableDomainFromEmail(rpClean);
            if (!string.IsNullOrEmpty(rpDom))
            {
                senderDomains.Add(rpDom);
            }
        }

        // 3. Inspect List-Unsubscribe & List-Unsubscribe-Post multiplicity (RFC 6376 § 5.4.2 guard)
        var listUnsubHeaders = message.Headers.Where(h => h.Id == HeaderId.ListUnsubscribe).ToList();
        var listUnsubPostHeaders = message.Headers.Where(h => h.Id == HeaderId.ListUnsubscribePost).ToList();

        if (listUnsubHeaders.Count > 1 || listUnsubPostHeaders.Count > 1)
        {
            report.SingleUnambiguousHeader = false;
            report.Provenance = AuthenticationProvenance.AmbiguousOrForgedHeader;
            AddUniqueSignal(report.PoisonSignals,
                "Multiple List-Unsubscribe or List-Unsubscribe-Post headers detected (RFC 6376 header-prepending risk).");
        }

        if (listUnsubPostHeaders.Count == 1)
        {
            var postVal = StripRfc5322Comments(listUnsubPostHeaders[0].Value).Trim();
            report.HasRfc8058OneClickPost = postVal.Contains("List-Unsubscribe=One-Click", StringComparison.OrdinalIgnoreCase);
            if (!report.HasRfc8058OneClickPost)
            {
                AddUniqueSignal(report.PoisonSignals,
                    "Malformed List-Unsubscribe-Post header (expected 'List-Unsubscribe=One-Click' per RFC 8058).");
            }
        }

        // 4. Evaluate DKIM-Signature & Topmost Authentication-Results (RFC 8601 + exact signature binding)
        var dkimSignatures = ParseDkimSignatures(message);
        var verifiedDkimDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!isSyntheticPaste)
        {
            EvaluateAuthenticationAndDkimBinding(
                message,
                dkimSignatures,
                hasListUnsubHeader: listUnsubHeaders.Count == 1,
                hasListUnsubPostHeader: listUnsubPostHeaders.Count == 1 && report.HasRfc8058OneClickPost,
                senderDomains,
                report,
                verifiedDkimDomains);
        }

        // 5. Extract & evaluate Header List-Unsubscribe candidates
        if (listUnsubHeaders.Count >= 1)
        {
            foreach (var hdr in listUnsubHeaders)
            {
                var rawTokens = ExtractAngleBracketTokens(StripRfc5322Comments(hdr.Value));
                if (rawTokens.Count == 0 && !string.IsNullOrWhiteSpace(hdr.Value))
                {
                    AddUniqueSignal(report.PoisonSignals,
                        "Malformed List-Unsubscribe header lacks RFC 2369 angle-bracketed <URI> syntax.");
                }

                foreach (var token in rawTokens)
                {
                    if (report.Candidates.Count >= MaxUnsubscribeCandidates) break;
                    var candidate = EvaluateUriCandidate(
                        rawUri: token,
                        source: "HeaderListUnsubscribe",
                        anchorText: string.Empty,
                        senderDomains: senderDomains,
                        verifiedDkimDomains: verifiedDkimDomains,
                        allowKnownEsp: report.AuthResultsClaimsDkimPass || report.AuthResultsClaimsSpfPass,
                        poisonSignals: report.PoisonSignals,
                        alignmentReasonOut: out var reason);

                    if (candidate.AlignedWithSenderOrEsp && report.AlignmentReason == null && reason != null)
                    {
                        report.AlignedWithSenderOrKnownEsp = true;
                        report.AlignmentReason = reason;
                    }

                    report.Candidates.Add(candidate);
                }
            }
        }

        // 6. Extract & evaluate HTML body <a href> unsubscribe candidates
        var htmlBody = message.HtmlBody;
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            var boundedHtml = htmlBody.Length > MaxHtmlScanLength ? htmlBody[..MaxHtmlScanLength] : htmlBody;
            var anchors = ExtractHtmlAnchorsLinear(boundedHtml);
            foreach (var (href, visibleText, attrsContext) in anchors)
            {
                if (report.Candidates.Count >= MaxUnsubscribeCandidates) break;

                if (!IsUnsubscribeAnchor(href, visibleText, attrsContext))
                {
                    continue;
                }

                var candidate = EvaluateUriCandidate(
                    rawUri: href,
                    source: "BodyHtmlAnchor",
                    anchorText: visibleText,
                    senderDomains: senderDomains,
                    verifiedDkimDomains: verifiedDkimDomains,
                    allowKnownEsp: report.AuthResultsClaimsDkimPass || report.AuthResultsClaimsSpfPass,
                    poisonSignals: report.PoisonSignals,
                    alignmentReasonOut: out var reason);

                if (candidate.AlignedWithSenderOrEsp && report.AlignmentReason == null && reason != null)
                {
                    report.AlignedWithSenderOrKnownEsp = true;
                    report.AlignmentReason = reason;
                }

                report.Candidates.Add(candidate);
            }
        }
        else if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            // Plain-text fallback when no HTML body exists
            ExtractPlainTextUnsubscribeCandidates(
                message.TextBody,
                senderDomains,
                verifiedDkimDomains,
                allowKnownEsp: report.AuthResultsClaimsDkimPass || report.AuthResultsClaimsSpfPass,
                report);
        }

        // 7. Cross-layer asymmetry check: Full email with unaligned body unsubscribe link & zero List-Unsubscribe header
        bool hasAnyHeaderCandidate = report.Candidates.Any(c => c.Source == "HeaderListUnsubscribe");
        var bodyCandidates = report.Candidates.Where(c => c.Source != "HeaderListUnsubscribe").ToList();

        if (report.HeaderContextAvailable && !hasAnyHeaderCandidate && bodyCandidates.Count > 0)
        {
            bool anyUnalignedBodyLink = bodyCandidates.Any(c => !c.AlignedWithSenderOrEsp);
            if (anyUnalignedBodyLink)
            {
                AddUniqueSignal(report.PoisonSignals,
                    "Body contains an unaligned 'Unsubscribe' link while RFC 2369/8058 List-Unsubscribe headers are completely absent.");
            }
        }

        if (report.HasRfc8058OneClickPost && !report.Candidates.Any(c => c.Source == "HeaderListUnsubscribe" && c.Scheme == "https"))
        {
            AddUniqueSignal(report.PoisonSignals,
                "List-Unsubscribe-Post One-Click header present without a required https:// URI in List-Unsubscribe (RFC 8058 violation).");
        }

        // 8. Finalize classification, risk score, recon warning, and sterile curl / mailto outputs
        FinalizeVerdict(report);
        return report;
    }

    private static void EvaluateAuthenticationAndDkimBinding(
        MimeMessage message,
        List<ParsedDkimSignature> dkimSignatures,
        bool hasListUnsubHeader,
        bool hasListUnsubPostHeader,
        HashSet<string> senderDomains,
        UnsubscribeForensicReport report,
        HashSet<string> verifiedDkimDomains)
    {
        int receivedCount = message.Headers.Count(h => h.Id == HeaderId.Received);
        var topAuthHeader = message.Headers.FirstOrDefault(h => h.Id == HeaderId.AuthenticationResults);

        if (topAuthHeader != null)
        {
            if (receivedCount == 0)
            {
                // Pre-populated Authentication-Results without any MTA Received hop
                if (report.Provenance != AuthenticationProvenance.AmbiguousOrForgedHeader)
                {
                    report.Provenance = AuthenticationProvenance.AmbiguousOrForgedHeader;
                }
                AddUniqueSignal(report.PoisonSignals,
                    "Authentication-Results header present without any Received MTA transit hops (potential pre-transit header forgery).");
                return;
            }

            if (report.Provenance != AuthenticationProvenance.AmbiguousOrForgedHeader)
            {
                report.Provenance = AuthenticationProvenance.UnverifiedUpload;
            }

            var authClean = StripRfc5322Comments(topAuthHeader.Value);
            var clauses = authClean.Split(';', StringSplitOptions.RemoveEmptyEntries);

            // Clause 0 in RFC 8601 is the authserv-id; subsequent clauses are method=result
            for (int i = 1; i < clauses.Length; i++)
            {
                var clause = clauses[i].Trim();
                if (StartsWithMethodPass(clause, "spf"))
                {
                    report.AuthResultsClaimsSpfPass = true;
                }
                else if (StartsWithMethodPass(clause, "dmarc"))
                {
                    report.AuthResultsClaimsDmarcPass = true;
                }
                else if (StartsWithMethodPass(clause, "dkim"))
                {
                    var headerD = ExtractAuthResultProp(clause, "header.d");
                    if (string.IsNullOrEmpty(headerD))
                    {
                        var headerI = ExtractAuthResultProp(clause, "header.i");
                        if (!string.IsNullOrEmpty(headerI) && headerI.Contains('@'))
                        {
                            headerD = headerI[(headerI.IndexOf('@') + 1)..];
                        }
                    }

                    var headerS = ExtractAuthResultProp(clause, "header.s");
                    var headerB = ExtractAuthResultProp(clause, "header.b");

                    if (string.IsNullOrWhiteSpace(headerD))
                    {
                        // Cannot bind dkim=pass without signing domain
                        continue;
                    }

                    var normalizedD = NormalizeDomain(headerD);
                    var candidates = dkimSignatures
                        .Where(s => string.Equals(s.Domain, normalizedD, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (!string.IsNullOrWhiteSpace(headerS))
                    {
                        candidates = candidates
                            .Where(s => string.Equals(s.Selector, headerS, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }

                    if (!string.IsNullOrWhiteSpace(headerB))
                    {
                        var cleanPrefix = RemoveAllWhitespace(headerB);
                        candidates = candidates
                            .Where(s => s.SignatureBase64.StartsWith(cleanPrefix, StringComparison.Ordinal))
                            .ToList();
                    }

                    if (candidates.Count == 1)
                    {
                        report.AuthResultsClaimsDkimPass = true;
                        var boundSig = candidates[0];
                        verifiedDkimDomains.Add(boundSig.Domain);

                        if (hasListUnsubHeader && SignatureCoversUnsubscribe(boundSig, hasListUnsubPostHeader))
                        {
                            report.DkimCoversUnsubscribeHeaders = true;
                        }
                    }
                    else if (candidates.Count > 1)
                    {
                        // Ambiguous multiple DKIM-Signatures with identical d= (and un-disambiguated s=/b=)
                        report.Provenance = AuthenticationProvenance.AmbiguousOrForgedHeader;
                        report.DkimCoversUnsubscribeHeaders = false;
                        AddUniqueSignal(report.PoisonSignals,
                            $"Ambiguous duplicate DKIM-Signature headers for '{normalizedD}'; refusing to trust h= unsubscribe coverage.");
                    }
                }
            }
        }
        else
        {
            // No Authentication-Results header (e.g., raw exported message without MX header)
            if (report.Provenance != AuthenticationProvenance.AmbiguousOrForgedHeader)
            {
                report.Provenance = AuthenticationProvenance.MissingAuthHeaders;
            }

            // Only recognize structural h= coverage when a single unambiguous DKIM-Signature aligns with From:
            if (dkimSignatures.Count == 1 && hasListUnsubHeader)
            {
                var soleSig = dkimSignatures[0];
                var sigRegDomain = GetRegistrableDomain(soleSig.Domain);
                if (!string.IsNullOrEmpty(sigRegDomain) && senderDomains.Contains(sigRegDomain))
                {
                    verifiedDkimDomains.Add(soleSig.Domain);
                    if (SignatureCoversUnsubscribe(soleSig, hasListUnsubPostHeader))
                    {
                        report.DkimCoversUnsubscribeHeaders = true;
                    }
                }
            }
            else if (dkimSignatures.Count > 1 && hasListUnsubHeader)
            {
                // Cannot disambiguate multiple DKIM signatures without trusted Authentication-Results
                report.DkimCoversUnsubscribeHeaders = false;
            }
        }
    }

    private static bool SignatureCoversUnsubscribe(ParsedDkimSignature sig, bool requirePostCoverage)
    {
        bool coversListUnsub = sig.SignedHeaders.Contains("list-unsubscribe");
        if (!coversListUnsub) return false;
        if (requirePostCoverage && !sig.SignedHeaders.Contains("list-unsubscribe-post")) return false;
        return true;
    }

    private static UnsubscribeCandidate EvaluateUriCandidate(
        string rawUri,
        string source,
        string anchorText,
        HashSet<string> senderDomains,
        HashSet<string> verifiedDkimDomains,
        bool allowKnownEsp,
        List<string> poisonSignals,
        out string? alignmentReasonOut)
    {
        alignmentReasonOut = null;
        var trimmed = rawUri.Trim();
        var candidate = new UnsubscribeCandidate
        {
            Source = source,
            AnchorText = TruncateClean(anchorText, 120)
        };

        if (string.IsNullOrEmpty(trimmed))
        {
            candidate.Scheme = "invalid";
            candidate.RiskFlags.Add("Empty unsubscribe URI");
            return candidate;
        }

        // Control character check
        if (trimmed.Any(c => c < 0x20 || c == 0x7F))
        {
            candidate.Scheme = "dangerous";
            candidate.SanitizedUri = "[redacted: contains control characters]";
            candidate.RiskFlags.Add("Contains ASCII control characters");
            AddUniqueSignal(poisonSignals, "Unsubscribe URI contains ASCII control characters (injection risk).");
            return candidate;
        }

        // Check executable / local pseudo-schemes before Uri.TryCreate
        int colonIdx = trimmed.IndexOf(':');
        if (colonIdx > 0)
        {
            var rawScheme = trimmed[..colonIdx].Trim().ToLowerInvariant();
            if (rawScheme is "javascript" or "data" or "vbscript" or "file" or "blob")
            {
                candidate.Scheme = "dangerous";
                candidate.SanitizedUri = $"{rawScheme}:[blocked-payload]";
                candidate.RiskFlags.Add($"Executable or local scheme '{rawScheme}:'");
                AddUniqueSignal(poisonSignals, $"Unsubscribe target uses dangerous '{rawScheme}:' URI scheme.");
                return candidate;
            }
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            candidate.Scheme = "invalid";
            candidate.SanitizedUri = TruncateClean(trimmed, 120);
            candidate.RiskFlags.Add("Malformed or relative URI");
            if (source == "HeaderListUnsubscribe")
            {
                AddUniqueSignal(poisonSignals, "List-Unsubscribe header contains a malformed or relative URI.");
            }
            return candidate;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        candidate.Scheme = scheme;

        if (scheme == "mailto")
        {
            return EvaluateMailtoCandidate(uri, candidate, senderDomains, verifiedDkimDomains, allowKnownEsp, poisonSignals, out alignmentReasonOut);
        }

        if (scheme is not ("https" or "http"))
        {
            candidate.SanitizedUri = $"{scheme}:[unsupported]";
            candidate.RiskFlags.Add($"Unsupported URI scheme '{scheme}:'");
            AddUniqueSignal(poisonSignals, $"Unsubscribe link uses unsupported '{scheme}:' scheme.");
            return candidate;
        }

        if (scheme == "http")
        {
            candidate.RiskFlags.Add("Unencrypted http:// scheme");
        }

        // Check UserInfo trick (https://apple.com@evil.example/unsub)
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            candidate.RiskFlags.Add("Embedded userinfo '@' credential spoofing");
            AddUniqueSignal(poisonSignals, "Unsubscribe URL embeds '@' userinfo credentials to disguise destination host.");
        }

        // Hostname & IDN / Punycode / Raw IP inspection
        var rawHost = uri.IdnHost;
        var displayHost = uri.Host;
        candidate.Host = rawHost.ToLowerInvariant();
        candidate.SanitizedUri = uri.AbsoluteUri;

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || IPAddress.TryParse(rawHost.Trim('[', ']'), out _))
        {
            candidate.RiskFlags.Add("Raw IP address literal instead of domain name");
            AddUniqueSignal(poisonSignals, $"Unsubscribe link targets a raw IP address ({rawHost}) instead of a verifiable domain.");
        }

        if (HasPunycodeOrMixedHomograph(rawHost, displayHost))
        {
            candidate.RiskFlags.Add("IDN punycode (xn--) or non-ASCII homograph domain");
            AddUniqueSignal(poisonSignals, $"Unsubscribe link uses IDN punycode / potential homograph domain ({rawHost}).");
        }

        if (!uri.IsDefaultPort && uri.Port != 443 && uri.Port != 80)
        {
            candidate.RiskFlags.Add($"Non-standard TCP port ({uri.Port})");
            AddUniqueSignal(poisonSignals, $"Unsubscribe link targets non-standard port {uri.Port}.");
        }

        var regDomain = GetRegistrableDomain(rawHost);
        candidate.RegistrableDomain = regDomain;

        if (!string.IsNullOrEmpty(regDomain) && UrlShortenerDomains.Contains(regDomain))
        {
            candidate.RiskFlags.Add($"Opaque URL shortener ({regDomain})");
            AddUniqueSignal(poisonSignals, $"Unsubscribe link is masked behind URL shortener '{regDomain}'.");
        }

        // Visual anchor-text domain spoofing check (e.g., <a href="https://evil.ru/u">https://apple.com/unsubscribe</a>)
        if (!string.IsNullOrWhiteSpace(anchorText) && !string.IsNullOrEmpty(regDomain))
        {
            var claimedDomain = ExtractDomainClaimedInAnchorText(anchorText);
            if (!string.IsNullOrEmpty(claimedDomain) &&
                !string.Equals(claimedDomain, regDomain, StringComparison.OrdinalIgnoreCase))
            {
                candidate.RiskFlags.Add($"Anchor text claims '{claimedDomain}' while href points to '{regDomain}'");
                AddUniqueSignal(poisonSignals,
                    $"Deceptive anchor text displays '{claimedDomain}' but actually links to '{regDomain}'.");
            }
        }

        // Check 3-tier sender / DKIM / Known-ESP alignment
        if (TryMatchTrustAlignment(rawHost, regDomain, senderDomains, verifiedDkimDomains, allowKnownEsp, out var alignReason))
        {
            candidate.AlignedWithSenderOrEsp = true;
            alignmentReasonOut = alignReason;
        }
        else
        {
            candidate.RiskFlags.Add("Domain unaligned with From, Return-Path, or authenticated ESP");
        }

        return candidate;
    }

    private static UnsubscribeCandidate EvaluateMailtoCandidate(
        Uri uri,
        UnsubscribeCandidate candidate,
        HashSet<string> senderDomains,
        HashSet<string> verifiedDkimDomains,
        bool allowKnownEsp,
        List<string> poisonSignals,
        out string? alignmentReasonOut)
    {
        alignmentReasonOut = null;
        var addressPart = $"{uri.UserInfo}@{uri.IdnHost}".TrimStart('@');
        if (string.IsNullOrWhiteSpace(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            candidate.RiskFlags.Add("Malformed mailto recipient");
            candidate.SanitizedUri = "mailto:[invalid]";
            return candidate;
        }

        var rawHost = uri.IdnHost.ToLowerInvariant();
        var regDomain = GetRegistrableDomain(rawHost);
        candidate.Host = rawHost;
        candidate.RegistrableDomain = regDomain;

        if (HasPunycodeOrMixedHomograph(rawHost, uri.Host))
        {
            candidate.RiskFlags.Add("IDN punycode mailto host");
            AddUniqueSignal(poisonSignals, $"Mailto unsubscribe target uses IDN punycode domain ({rawHost}).");
        }

        // Sanitize RFC 6068 query parameters (allow strictly subject & body; flag bcc/cc/attach abuse)
        var allowedParams = new List<string>();
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var rawQuery = uri.Query.TrimStart('?');
            foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIdx = pair.IndexOf('=');
                var key = (eqIdx >= 0 ? pair[..eqIdx] : pair).Trim().ToLowerInvariant();
                var val = eqIdx >= 0 ? pair[(eqIdx + 1)..] : string.Empty;

                if (key is "subject" or "body")
                {
                    var decoded = Uri.UnescapeDataString(val);
                    if (decoded.Any(c => c < 0x20 && c != '\r' && c != '\n' && c != '\t'))
                    {
                        candidate.RiskFlags.Add($"Control characters stripped from mailto '{key}'");
                        continue;
                    }
                    allowedParams.Add($"{key}={Uri.EscapeDataString(TruncateClean(decoded, 200))}");
                }
                else if (key is "bcc" or "cc" or "attach" or "attachment")
                {
                    candidate.RiskFlags.Add($"Dangerous mailto parameter '{key}' stripped");
                    AddUniqueSignal(poisonSignals, $"Mailto unsubscribe URI attempted to inject '{key}=' header parameter.");
                }
            }
        }

        candidate.SanitizedUri = allowedParams.Count > 0
            ? $"mailto:{addressPart}?{string.Join("&", allowedParams)}"
            : $"mailto:{addressPart}";

        if (TryMatchTrustAlignment(rawHost, regDomain, senderDomains, verifiedDkimDomains, allowKnownEsp, out var alignReason))
        {
            candidate.AlignedWithSenderOrEsp = true;
            alignmentReasonOut = alignReason;
        }
        else
        {
            candidate.RiskFlags.Add("Mailto domain unaligned with sender or authenticated ESP");
        }

        return candidate;
    }

    private static void FinalizeVerdict(UnsubscribeForensicReport report)
    {
        if (report.PoisonSignals.Count > 0)
        {
            report.Classification = UnsubscribeClassification.PoisonOrReconTrap;
            report.RiskScore = Math.Min(100, 75 + (report.PoisonSignals.Count * 8));
            report.SummaryVerdict = "High-risk or poisoned unsubscribe pattern detected. Do not click or contact this endpoint.";
            report.ReconWarning = "Blocked: contacting a suspicious unsubscribe target confirms your email address is active to a potential threat actor.";
            report.SterileCurlCommand = null;
            report.SanitizedMailtoUri = null;
            return;
        }

        if (report.Candidates.Count == 0)
        {
            report.Classification = UnsubscribeClassification.NoUnsubscribeFound;
            report.RiskScore = 0;
            report.SummaryVerdict = report.HeaderContextAvailable
                ? "No List-Unsubscribe headers or opt-out links detected in this email."
                : "Pasted text snippet contains no unsubscribe links (paste full RFC 822 source to inspect headers).";
            report.ReconWarning = string.Empty;
            return;
        }

        // Check qualifying RFC 8058 One-Click POST candidate
        var httpsHeaderCandidate = report.Candidates.FirstOrDefault(c =>
            c.Source == "HeaderListUnsubscribe" &&
            c.Scheme == "https" &&
            c.AlignedWithSenderOrEsp &&
            c.RiskFlags.Count == 0);

        if (httpsHeaderCandidate != null &&
            report.SingleUnambiguousHeader &&
            report.HasRfc8058OneClickPost &&
            report.DkimCoversUnsubscribeHeaders)
        {
            var curlCmd = TryBuildSterileCurlCommand(httpsHeaderCandidate.SanitizedUri);
            if (curlCmd != null)
            {
                report.Classification = UnsubscribeClassification.OneClickPostCandidate;
                report.RiskScore = report.AuthResultsClaimsDkimPass ? 10 : 22;
                report.PrimaryTargetHost = httpsHeaderCandidate.Host;
                report.SterileCurlCommand = curlCmd;
                report.ReconWarning = MandatoryReconWarning;
                report.SummaryVerdict = report.Provenance == AuthenticationProvenance.UnverifiedUpload
                    ? $"RFC 8058 One-Click POST candidate ({httpsHeaderCandidate.Host}) — static header checks pass (uploaded .eml provenance unverified)."
                    : $"RFC 8058 One-Click POST candidate ({httpsHeaderCandidate.Host}) — DKIM header binding present (MX Authentication-Results absent).";
                return;
            }
        }

        // Check qualifying RFC 2369 Mailto candidate
        var mailtoHeaderCandidate = report.Candidates.FirstOrDefault(c =>
            c.Source == "HeaderListUnsubscribe" &&
            c.Scheme == "mailto" &&
            c.AlignedWithSenderOrEsp &&
            c.RiskFlags.Count == 0);

        if (mailtoHeaderCandidate != null && report.SingleUnambiguousHeader)
        {
            report.Classification = UnsubscribeClassification.MailtoCandidate;
            report.RiskScore = report.DkimCoversUnsubscribeHeaders ? 18 : 30;
            report.PrimaryTargetHost = mailtoHeaderCandidate.Host;
            report.SanitizedMailtoUri = mailtoHeaderCandidate.SanitizedUri;
            report.ReconWarning = MandatoryReconWarning;
            report.SummaryVerdict = $"Out-of-band mailto unsubscribe candidate ({mailtoHeaderCandidate.Host}) — requires sending plain-text opt-out email.";
            return;
        }

        // Otherwise: HTTP landing page or unverified/unsigned header or pasted body link
        var primary = report.Candidates[0];
        report.Classification = UnsubscribeClassification.HttpLandingCandidate;
        report.PrimaryTargetHost = primary.Host;
        report.ReconWarning = MandatoryReconWarning;

        bool hasUnaligned = report.Candidates.Any(c => !c.AlignedWithSenderOrEsp);
        bool hasHttpDowngrade = report.Candidates.Any(c => c.Scheme == "http");
        report.RiskScore = hasUnaligned ? 58 : (hasHttpDowngrade ? 48 : 35);

        if (!report.HeaderContextAvailable)
        {
            report.SummaryVerdict = $"Body unsubscribe link detected ({primary.Host}) in pasted snippet — paste full RFC 822 headers to evaluate DKIM & RFC 8058.";
        }
        else if (report.HasRfc8058OneClickPost && !report.DkimCoversUnsubscribeHeaders)
        {
            report.SummaryVerdict = $"One-Click POST suppressed for {primary.Host}: List-Unsubscribe is not covered by an unambiguous DKIM-Signature (RFC 8058 § 3.1).";
        }
        else
        {
            report.SummaryVerdict = $"Interactive web landing link ({primary.Host}) — no RFC 8058 one-click POST header available.";
        }
    }

    private static string? TryBuildSterileCurlCommand(string candidateUri)
    {
        if (!Uri.TryCreate(candidateUri, UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (!uri.IsDefaultPort && uri.Port != 443) return null;

        var encoded = uri.AbsoluteUri;
        if (encoded.Any(c => c <= 0x20 || c >= 0x7F))
        {
            return null;
        }

        // POSIX single-quote escaping: replace ' with '\''
        var shellSafeUri = encoded.Replace("'", "'\\''", StringComparison.Ordinal);
        return $"curl --globoff --proto '=https' --tlsv1.2 --max-redirs 0 -sS -o /dev/null -w '%{{http_code}}\\n' -X POST -H 'Content-Type: application/x-www-form-urlencoded' -d 'List-Unsubscribe=One-Click' '{shellSafeUri}'";
    }

    private static bool TryMatchTrustAlignment(
        string rawHost,
        string regDomain,
        HashSet<string> senderDomains,
        HashSet<string> verifiedDkimDomains,
        bool allowKnownEsp,
        out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(regDomain)) return false;

        if (senderDomains.Contains(regDomain))
        {
            reason = $"Matches sender domain ({regDomain})";
            return true;
        }

        foreach (var dkimDom in verifiedDkimDomains)
        {
            if (string.Equals(rawHost, dkimDom, StringComparison.OrdinalIgnoreCase) ||
                rawHost.EndsWith("." + dkimDom, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(regDomain, GetRegistrableDomain(dkimDom), StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Matches DKIM signing domain ({dkimDom})";
                return true;
            }
        }

        if (allowKnownEsp && KnownEspDomains.Contains(regDomain))
        {
            reason = $"Known bulk ESP infrastructure ({regDomain}) with passing MTA auth assertion";
            return true;
        }

        return false;
    }

    private static bool HasPunycodeOrMixedHomograph(string asciiHost, string unicodeHost)
    {
        if (asciiHost.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) ||
            asciiHost.Contains(".xn--", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (unicodeHost.Any(c => c > 127))
        {
            return true;
        }

        try
        {
            var idn = new IdnMapping();
            var ascii = idn.GetAscii(unicodeHost);
            if (ascii.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) ||
                ascii.Contains(".xn--", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (ArgumentException)
        {
            return true;
        }

        return false;
    }

    private static string? ExtractDomainClaimedInAnchorText(string anchorText)
    {
        var text = anchorText.Trim();
        if (text.Length < 4 || !text.Contains('.')) return null;

        // Check if anchor text contains an explicit http(s):// URL
        var urlMatch = PlainUrlRegex().Match(text);
        if (urlMatch.Success && Uri.TryCreate(urlMatch.Value, UriKind.Absolute, out var claimedUri))
        {
            return GetRegistrableDomain(claimedUri.IdnHost);
        }

        // Or if the entire anchor text looks like a bare domain/path (e.g., "apple.com/unsubscribe")
        var bareMatch = BareDomainTextRegex().Match(text);
        if (bareMatch.Success)
        {
            var hostPart = bareMatch.Groups[1].Value;
            return GetRegistrableDomain(hostPart);
        }

        return null;
    }

    public static string GetRegistrableDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;
        var clean = host.Trim().Trim('.').ToLowerInvariant();
        if (IPAddress.TryParse(clean.Trim('[', ']'), out _)) return string.Empty;

        var labels = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2) return clean;

        var lastTwo = $"{labels[^2]}.{labels[^1]}";
        if (KnownMultiPartPublicSuffixes.Contains(lastTwo))
        {
            return labels.Length >= 3 ? $"{labels[^3]}.{lastTwo}" : clean;
        }

        return lastTwo;
    }

    private static string ExtractRegistrableDomainFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        int atIdx = email.LastIndexOf('@');
        if (atIdx < 0 || atIdx == email.Length - 1) return string.Empty;
        var domain = email[(atIdx + 1)..].Trim().Trim('>');
        return GetRegistrableDomain(domain);
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().Trim('.').ToLowerInvariant();
    }

    private static List<ParsedDkimSignature> ParseDkimSignatures(MimeMessage message)
    {
        var results = new List<ParsedDkimSignature>();
        foreach (var header in message.Headers.Where(h => h.Id == HeaderId.DkimSignature))
        {
            var clean = StripRfc5322Comments(header.Value);
            string domain = string.Empty;
            string selector = string.Empty;
            string bValue = string.Empty;
            var signedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in clean.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var tag = part[..eq].Trim().ToLowerInvariant();
                var val = part[(eq + 1)..].Trim();

                switch (tag)
                {
                    case "d":
                        domain = NormalizeDomain(val);
                        break;
                    case "s":
                        selector = RemoveAllWhitespace(val);
                        break;
                    case "b":
                        bValue = RemoveAllWhitespace(val);
                        break;
                    case "h":
                        foreach (var hName in val.Split(':', StringSplitOptions.RemoveEmptyEntries))
                        {
                            signedHeaders.Add(hName.Trim().ToLowerInvariant());
                        }
                        break;
                }
            }

            if (!string.IsNullOrEmpty(domain))
            {
                results.Add(new ParsedDkimSignature(domain, selector, bValue, signedHeaders));
            }
        }

        return results;
    }

    private static bool StartsWithMethodPass(string clause, string method)
    {
        var match = AuthMethodResultRegex().Match(clause);
        return match.Success &&
               string.Equals(match.Groups[1].Value, method, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(match.Groups[2].Value, "pass", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractAuthResultProp(string clause, string propName)
    {
        int searchFrom = 0;
        while (searchFrom < clause.Length)
        {
            int idx = clause.IndexOf(propName, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            // Enforce left token boundary so 'x-header.d' never matches 'header.d'
            if (idx > 0 && (char.IsLetterOrDigit(clause[idx - 1]) || clause[idx - 1] is '.' or '-' or '_'))
            {
                searchFrom = idx + propName.Length;
                continue;
            }

            int pos = idx + propName.Length;
            while (pos < clause.Length && char.IsWhiteSpace(clause[pos])) pos++;
            if (pos >= clause.Length || clause[pos] != '=')
            {
                searchFrom = pos;
                continue;
            }

            int start = pos + 1;
            while (start < clause.Length && char.IsWhiteSpace(clause[start])) start++;
            if (start >= clause.Length) return null;

            if (clause[start] == '"')
            {
                int endQuote = clause.IndexOf('"', start + 1);
                return endQuote > start ? clause[(start + 1)..endQuote] : clause[(start + 1)..];
            }

            int end = start;
            while (end < clause.Length && !char.IsWhiteSpace(clause[end]) && clause[end] != ';')
            {
                end++;
            }
            return clause[start..end].Trim();
        }

        return null;
    }

    private static string StripRfc5322Comments(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains('(')) return input;
        var sb = new StringBuilder(input.Length);
        int depth = 0;
        bool escaped = false;

        foreach (char c in input)
        {
            if (escaped)
            {
                escaped = false;
                if (depth == 0) sb.Append(c);
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                if (depth == 0) sb.Append(c);
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static List<string> ExtractAngleBracketTokens(string headerValue)
    {
        var list = new List<string>();
        int pos = 0;
        while (pos < headerValue.Length && list.Count < MaxUnsubscribeCandidates)
        {
            int open = headerValue.IndexOf('<', pos);
            if (open < 0) break;
            int close = headerValue.IndexOf('>', open + 1);
            if (close < 0) break;

            var inner = headerValue[(open + 1)..close].Trim();
            // Folded CRLF + whitespace inside angle brackets in RFC 2369 headers
            inner = RemoveCrLfFolding(inner);
            if (!string.IsNullOrEmpty(inner))
            {
                list.Add(inner);
            }
            pos = close + 1;
        }
        return list;
    }

    private static List<(string Href, string VisibleText, string Attrs)> ExtractHtmlAnchorsLinear(string html)
    {
        var results = new List<(string, string, string)>();
        int idx = 0;

        while (idx < html.Length && results.Count < 200)
        {
            int tagStart = html.IndexOf("<a", idx, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0) break;

            // Ensure '<a' is followed by whitespace or '>' (not '<abbr>' or '<article>')
            if (tagStart + 2 >= html.Length) break;
            char afterA = html[tagStart + 2];
            if (!char.IsWhiteSpace(afterA) && afterA != '>' && afterA != '/')
            {
                idx = tagStart + 2;
                continue;
            }

            int tagEnd = FindTagCloseQuoteAware(html, tagStart + 2);
            if (tagEnd < 0) break;

            var attrs = html[(tagStart + 2)..tagEnd];
            var href = ExtractHrefAttribute(attrs);

            int closeAnchor = html.IndexOf("</a", tagEnd + 1, StringComparison.OrdinalIgnoreCase);
            string innerHtml = closeAnchor > tagEnd
                ? html[(tagEnd + 1)..Math.Min(closeAnchor, tagEnd + 2000)]
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(href))
            {
                var visible = StripTagsLinear(WebUtility.HtmlDecode(innerHtml));
                results.Add((WebUtility.HtmlDecode(href).Trim(), visible, attrs));
            }

            idx = closeAnchor > tagEnd ? closeAnchor + 3 : tagEnd + 1;
        }

        return results;
    }

    private static int FindTagCloseQuoteAware(string html, int start)
    {
        char quote = '\0';
        int limit = Math.Min(html.Length, start + 4096);
        for (int i = start; i < limit; i++)
        {
            char c = html[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }
        return -1;
    }

    private static string? ExtractHrefAttribute(string attrs)
    {
        int pos = 0;
        while (pos < attrs.Length)
        {
            int idx = attrs.IndexOf("href", pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            // Ensure word boundary before 'href' (not 'data-href')
            if (idx > 0 && (char.IsLetterOrDigit(attrs[idx - 1]) || attrs[idx - 1] is '-' or '_'))
            {
                pos = idx + 4;
                continue;
            }

            int i = idx + 4;
            while (i < attrs.Length && char.IsWhiteSpace(attrs[i])) i++;
            if (i >= attrs.Length || attrs[i] != '=')
            {
                pos = i;
                continue;
            }

            i++; // skip '='
            while (i < attrs.Length && char.IsWhiteSpace(attrs[i])) i++;
            if (i >= attrs.Length) return null;

            if (attrs[i] is '"' or '\'')
            {
                char q = attrs[i];
                int endQ = attrs.IndexOf(q, i + 1);
                return endQ > i ? attrs[(i + 1)..endQ] : null;
            }

            int endUnquoted = i;
            while (endUnquoted < attrs.Length && !char.IsWhiteSpace(attrs[endUnquoted])) endUnquoted++;
            return attrs[i..endUnquoted];
        }
        return null;
    }

    private static string StripTagsLinear(string snippet)
    {
        if (string.IsNullOrEmpty(snippet)) return string.Empty;
        var sb = new StringBuilder(snippet.Length);
        bool inTag = false;
        foreach (char c in snippet)
        {
            if (c == '<') { inTag = true; sb.Append(' '); }
            else if (c == '>') { inTag = false; }
            else if (!inTag) { sb.Append(c); }
        }
        return CollapseWhitespace(sb.ToString());
    }

    private static void ExtractPlainTextUnsubscribeCandidates(
        string textBody,
        HashSet<string> senderDomains,
        HashSet<string> verifiedDkimDomains,
        bool allowKnownEsp,
        UnsubscribeForensicReport report)
    {
        var bounded = textBody.Length > MaxHtmlScanLength ? textBody[..MaxHtmlScanLength] : textBody;
        var lines = bounded.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length && report.Candidates.Count < MaxUnsubscribeCandidates; i++)
        {
            var window = i > 0 ? $"{lines[i - 1]} {lines[i]}" : lines[i];
            if (!ContainsUnsubscribeKeyword(window)) continue;

            foreach (Match m in PlainUrlRegex().Matches(lines[i]))
            {
                if (report.Candidates.Count >= MaxUnsubscribeCandidates) break;
                var rawUrl = m.Value.TrimEnd('.', ',', ')', ']', ';', '>');
                var candidate = EvaluateUriCandidate(
                    rawUri: rawUrl,
                    source: "BodyPlainText",
                    anchorText: string.Empty,
                    senderDomains: senderDomains,
                    verifiedDkimDomains: verifiedDkimDomains,
                    allowKnownEsp: allowKnownEsp,
                    poisonSignals: report.PoisonSignals,
                    alignmentReasonOut: out var reason);

                if (candidate.AlignedWithSenderOrEsp && report.AlignmentReason == null && reason != null)
                {
                    report.AlignedWithSenderOrKnownEsp = true;
                    report.AlignmentReason = reason;
                }

                report.Candidates.Add(candidate);
            }
        }
    }

    private static bool IsUnsubscribeAnchor(string href, string visibleText, string attrs)
    {
        return ContainsUnsubscribeKeyword(visibleText) ||
               ContainsUnsubscribeKeyword(href) ||
               ContainsUnsubscribeKeyword(attrs) ||
               href.Contains("/unsub", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsUnsubscribeKeyword(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        foreach (var kw in UnsubscribeKeywords)
        {
            if (input.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string RemoveAllWhitespace(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string RemoveCrLfFolding(string input)
    {
        if (!input.Contains('\r') && !input.Contains('\n')) return input;
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c is '\r' or '\n')
            {
                while (i + 1 < input.Length && (input[i + 1] is ' ' or '\t' or '\r' or '\n'))
                {
                    i++;
                }
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string CollapseWhitespace(string input)
    {
        var sb = new StringBuilder(input.Length);
        bool prevSpace = false;
        foreach (char c in input)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace) { sb.Append(' '); prevSpace = true; }
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    private static string TruncateClean(string input, int maxLen)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var clean = CollapseWhitespace(input);
        return clean.Length <= maxLen ? clean : clean[..maxLen] + "...";
    }

    private static void AddUniqueSignal(List<string> signals, string signal)
    {
        if (!signals.Contains(signal, StringComparer.Ordinal))
        {
            signals.Add(signal);
        }
    }

    private sealed record ParsedDkimSignature(
        string Domain,
        string Selector,
        string SignatureBase64,
        HashSet<string> SignedHeaders);

    [GeneratedRegex(@"^\s*([a-z0-9_-]+)\s*=\s*([a-z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, 250)]
    private static partial Regex AuthMethodResultRegex();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, 250)]
    private static partial Regex PlainUrlRegex();

    [GeneratedRegex(@"^\s*([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+)(?:/[^\s]*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, 250)]
    private static partial Regex BareDomainTextRegex();
}
