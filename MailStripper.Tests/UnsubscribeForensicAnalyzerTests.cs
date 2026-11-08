using System.Text;
using MailStripper.Models;
using MailStripper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Xunit;

namespace MailStripper.Tests;

public class UnsubscribeForensicAnalyzerTests
{
    private readonly UnsubscribeForensicAnalyzer _analyzer = new();

    private static MimeMessage ParseRawEml(string rawEml)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(rawEml));
        return MimeMessage.Load(ms);
    }

    [Fact]
    public void Analyze_ConformingRfc8058Newsletter_ProducesOneClickCandidateAndHardenedCurl()
    {
        var bytes = SampleEmailService.GenerateSampleNewsletterEml();
        using var ms = new MemoryStream(bytes);
        var message = MimeMessage.Load(ms);

        var report = _analyzer.Analyze(message);

        Assert.Equal(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.Equal(AuthenticationProvenance.UnverifiedUpload, report.Provenance);
        Assert.True(report.SingleUnambiguousHeader);
        Assert.True(report.HasRfc8058OneClickPost);
        Assert.True(report.DkimCoversUnsubscribeHeaders);
        Assert.True(report.AuthResultsClaimsDkimPass);
        Assert.True(report.AlignedWithSenderOrKnownEsp);
        Assert.Empty(report.PoisonSignals);
        Assert.NotEmpty(report.ReconWarning);
        Assert.NotNull(report.SterileCurlCommand);
        Assert.Contains("--globoff", report.SterileCurlCommand);
        Assert.Contains("--proto '=https'", report.SterileCurlCommand);
        Assert.Contains("--max-redirs 0", report.SterileCurlCommand);
        Assert.Contains("-o /dev/null", report.SterileCurlCommand);
        Assert.Contains("-d 'List-Unsubscribe=One-Click'", report.SterileCurlCommand);
    }

    [Fact]
    public void Analyze_KnownEspWithPassingAuth_AlignsWhilePreservingReconWarning()
    {
        var eml =
            "Received: from mta.sendgrid.net (mta.sendgrid.net [149.72.1.1]) by mx.example.org;\r\n" +
            "Authentication-Results: mx.example.org; dkim=pass header.d=sendgrid.net header.s=smtpapi; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=sendgrid.net; s=smtpapi; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZGVm\r\n" +
            "From: Acme Cloud <updates@acme-cloud.io>\r\n" +
            "To: user@example.org\r\n" +
            "Subject: Monthly Product Changelog\r\n" +
            "List-Unsubscribe: <https://u9912.ct.sendgrid.net/wf/oneclick?upn=xyz>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Changelog body.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.True(report.AlignedWithSenderOrKnownEsp);
        Assert.Contains("mailbox is active", report.ReconWarning);
    }

    [Fact]
    public void GetRegistrableDomain_PreservesMultiLabelPublicSuffixes_PreventsFalseCoUkAlignment()
    {
        Assert.Equal("brand-alpha.co.uk", UnsubscribeForensicAnalyzer.GetRegistrableDomain("mail.brand-alpha.co.uk"));
        Assert.Equal("evil-other.co.uk", UnsubscribeForensicAnalyzer.GetRegistrableDomain("unsub.evil-other.co.uk"));

        var eml =
            "Received: from relay.evil-other.co.uk by mx.example.org;\r\n" +
            "From: Billing <billing@brand-alpha.co.uk>\r\n" +
            "To: user@example.org\r\n" +
            "Subject: Invoice Notice\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n\r\n" +
            "<p><a href='https://unsub.evil-other.co.uk/optout'>Unsubscribe</a></p>";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.False(report.AlignedWithSenderOrKnownEsp);
    }

    [Fact]
    public void Analyze_PrependedDuplicateListUnsubscribeHeader_FailsClosedAsPoisonTrap()
    {
        var eml =
            "Received: from mail.systems-digest.io by mx.example.org;\r\n" +
            "List-Unsubscribe: <https://attacker-injected.invalid/unsub>\r\n" +
            "Authentication-Results: mx.example.org; dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "Subject: Weekly Update\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.False(report.SingleUnambiguousHeader);
        Assert.Equal(AuthenticationProvenance.AmbiguousOrForgedHeader, report.Provenance);
        Assert.Null(report.SterileCurlCommand);
    }

    [Fact]
    public void Analyze_SecondForgedDkimSignatureWithDifferentSelector_DoesNotCreditUnsubscribeCoverage()
    {
        // Passing signature s=real does NOT cover list-unsubscribe; forged signature s=forged DOES claim h=list-unsubscribe
        var eml =
            "Received: from mail.systems-digest.io by mx.example.org;\r\n" +
            "Authentication-Results: mx.example.org; dkim=pass header.d=systems-digest.io header.s=real\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=real; h=from:to:subject; b=cmVhbFNpZw==\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=forged; h=from:list-unsubscribe:list-unsubscribe-post; b=Zm9yZ2Vk\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@example.org\r\n" +
            "Subject: Test Selector Binding\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.False(report.DkimCoversUnsubscribeHeaders);
        Assert.NotEqual(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.Null(report.SterileCurlCommand);
    }

    [Fact]
    public void Analyze_ForgedAuthResultsWithoutReceivedHops_FlagsPoisonOrReconTrap()
    {
        var eml =
            "Authentication-Results: mx.spoofed.local; dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJj\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "Subject: Crafted Local File\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.Equal(AuthenticationProvenance.AmbiguousOrForgedHeader, report.Provenance);
        Assert.Null(report.SterileCurlCommand);
    }

    [Fact]
    public void Analyze_PoisonedPhishingSample_DetectsPunycodeAndAnchorTextSpoofing()
    {
        var bytes = SampleEmailService.GenerateSamplePoisonTrapEml();
        using var ms = new MemoryStream(bytes);
        var message = MimeMessage.Load(ms);

        var report = _analyzer.Analyze(message);

        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.True(report.RiskScore >= 80);
        Assert.Contains(report.PoisonSignals, s => s.Contains("punycode", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.PoisonSignals, s => s.Contains("anchor text", StringComparison.OrdinalIgnoreCase));
        Assert.Null(report.SterileCurlCommand);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("https://185.199.108.153/unsubscribe")]
    [InlineData("https://vanguard-tech.io@credential-harvest.example/unsub")]
    [InlineData("https://bit.ly/3xYzPhish")]
    [InlineData("mailto:leave@vanguard-tech.io?subject=unsub&bcc=recon-drop@evil.example")]
    public void Analyze_MaliciousSchemesIpsUserInfoShortenersAndMailtoBcc_ClassifiedAsPoisonTrap(string maliciousTarget)
    {
        var eml =
            "Received: from mx1.vanguard-tech.io by mx.local;\r\n" +
            "From: Ops <ops@vanguard-tech.io>\r\n" +
            "To: dev@vanguard-tech.io\r\n" +
            "Subject: Alert\r\n" +
            $"List-Unsubscribe: <{maliciousTarget}>\r\n\r\n" +
            "Body text.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.NotEmpty(report.PoisonSignals);
        Assert.Null(report.SterileCurlCommand);
        Assert.Null(report.SanitizedMailtoUri);
    }

    [Fact]
    public void Analyze_SingleQuoteInHttpsQuery_EscapesPosixSingleQuotesSafelyInCurl()
    {
        var eml =
            "Received: from mta.systems-digest.io by mx.local;\r\n" +
            "Authentication-Results: mx.local; dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJj\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "Subject: Quote Escape Check\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/u?tok=abc'$(uname)def>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.NotNull(report.SterileCurlCommand);
        // Any literal single quote inside the URI must be escaped via '\'' so $(uname) stays inside single quotes
        Assert.Contains("'\\''", report.SterileCurlCommand);
    }

    [Fact]
    public async Task ParseTextAsync_SyntheticPastedSnippetWithBodyUnsubscribe_DoesNotFalsePositiveMissingHeaders()
    {
        using var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var snippet = "Weekly engineering digest.\nTo unsubscribe from future updates: https://systems-digest.io/unsubscribe?id=42";
        var result = await parser.ParseTextAsync(snippet);

        Assert.False(result.Unsubscribe.HeaderContextAvailable);
        Assert.Equal(AuthenticationProvenance.SyntheticPastedBody, result.Unsubscribe.Provenance);
        Assert.Equal(UnsubscribeClassification.HttpLandingCandidate, result.Unsubscribe.Classification);
        Assert.Empty(result.Unsubscribe.PoisonSignals);
    }

    [Fact]
    public void Analyze_ForwardedMessageRfc822Attachment_InspectsInnerMessageWithinDepthCap()
    {
        var innerBytes = SampleEmailService.GenerateSampleNewsletterEml();
        using var innerMs = new MemoryStream(innerBytes);
        var innerMsg = MimeMessage.Load(innerMs);

        var outer = new MimeMessage();
        outer.From.Add(new MailboxAddress("Colleague", "colleague@internal.example"));
        outer.To.Add(new MailboxAddress("Me", "me@internal.example"));
        outer.Subject = "Fwd: Check out this newsletter";

        var multipart = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Forwarding this issue along." },
            new MessagePart { Message = innerMsg }
        };
        outer.Body = multipart;

        var report = _analyzer.Analyze(outer);

        Assert.Equal(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.Equal("optout.systems-digest.io", report.PrimaryTargetHost);
    }

    [Fact]
    public void Analyze_VerifiedMailtoOnly_SanitizesRfc6068QueryParameters()
    {
        var eml =
            "Received: from mta.systems-digest.io by mx.local;\r\n" +
            "Authentication-Results: mx.local; dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe; b=YWJj\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "Subject: Mailto List\r\n" +
            "List-Unsubscribe: (Send mail) <mailto:leave@systems-digest.io?subject=unsubscribe&x-track=dropme>\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(UnsubscribeClassification.MailtoCandidate, report.Classification);
        Assert.Equal("mailto:leave@systems-digest.io?subject=unsubscribe", report.SanitizedMailtoUri);
    }

    [Fact]
    public void Analyze_HeaderBMismatchAgainstDkimSignature_SuppressesOneClickPost()
    {
        var eml =
            "Received: from mta.systems-digest.io by mx.local;\r\n" +
            "Authentication-Results: mx.local; dkim=pass header.d=systems-digest.io header.s=s1 header.b=AAAAAAAA\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=BBBBBBBBBBBB\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "Subject: Mismatched Signature Prefix\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml));

        Assert.False(report.AuthResultsClaimsDkimPass);
        Assert.False(report.DkimCoversUnsubscribeHeaders);
        Assert.NotEqual(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.Null(report.SterileCurlCommand);
    }

    [Fact]
    public void Analyze_DeeplyNestedMessageRfc822BeyondCap_HaltsRecursionBoundedly()
    {
        var leafBytes = SampleEmailService.GenerateSampleNewsletterEml();
        using var leafMs = new MemoryStream(leafBytes);
        MimeMessage current = MimeMessage.Load(leafMs);

        // Wrap 5 times (exceeding MaxForwardedDepth = 3)
        for (int i = 0; i < 5; i++)
        {
            var wrapper = new MimeMessage();
            wrapper.From.Add(new MailboxAddress("Forwarder", $"fwd{i}@internal.example"));
            wrapper.To.Add(new MailboxAddress("Analyst", "analyst@internal.example"));
            wrapper.Subject = $"Fwd level {i}";
            wrapper.Body = new Multipart("mixed")
            {
                new TextPart("plain") { Text = "Wrapper layer without unsubscribe text." },
                new MessagePart { Message = current }
            };
            current = wrapper;
        }

        var report = _analyzer.Analyze(current);

        // Recursion stops at depth 3 rather than descending unboundedly to depth 5
        Assert.Equal(UnsubscribeClassification.NoUnsubscribeFound, report.Classification);
    }

    [Fact]
    public void Analyze_WithMatchingTrustedAuthservId_ElevatesProvenanceToTrustedReceivingMx()
    {
        var eml =
            "Received: from mail.systems-digest.io by mx.corp.example;\r\n" +
            "Authentication-Results: mx.corp.example; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Systems Digest Issue 42\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Issue 42 content.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.corp.example");

        Assert.Equal(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.Equal(AuthenticationProvenance.TrustedReceivingMx, report.Provenance);
        Assert.True(report.AuthservIdVerified);
        Assert.Equal("mx.corp.example", report.MatchedAuthservId);
        Assert.Equal("mx.corp.example", report.ExpectedAuthservId);
        Assert.True(report.AuthResultsClaimsDkimPass);
        Assert.True(report.DkimCoversUnsubscribeHeaders);
        Assert.Equal(10, report.RiskScore);
        Assert.Contains("headers name expected receiving MX 'mx.corp.example'", report.SummaryVerdict);
        Assert.NotNull(report.SterileCurlCommand);
    }

    [Fact]
    public void Analyze_WithRFC5322CommentsAndVersionInAuthservId_MatchesCorrectly()
    {
        var eml =
            "Received: from relay.net by mx.corp.example;\r\n" +
            "Authentication-Results: (internal mta) mx.corp.example 1 (authres-v1); dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Weekly Brief\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Brief content.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "MX.CORP.EXAMPLE.");

        Assert.Equal(AuthenticationProvenance.TrustedReceivingMx, report.Provenance);
        Assert.True(report.AuthservIdVerified);
        Assert.Equal("mx.corp.example", report.MatchedAuthservId);
        Assert.Equal("mx.corp.example", report.ExpectedAuthservId);
    }

    [Fact]
    public void Analyze_WithMismatchedAuthservId_RefusesToTrustDkimPassAndFlagsWarning()
    {
        var eml =
            "Received: from attacker-relay.net by untrusted.mta;\r\n" +
            "Authentication-Results: mx.attacker-relay.net; dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Suspicious Header Assertion\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Content.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.trusted-corp.example");

        // Fails RFC 8601 authserv-id matching: cannot trust the attacker relay's auth assertion
        Assert.Equal(AuthenticationProvenance.UnverifiedUpload, report.Provenance);
        Assert.False(report.AuthservIdVerified);
        Assert.False(report.AuthResultsClaimsDkimPass);
        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.Contains(report.PoisonSignals, s => s.Contains("does not match expected trusted authserv-id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_WithMatchingAuthservId_ZeroReceivedHops_FailsClosedAsAmbiguousOrForged()
    {
        var eml =
            "Authentication-Results: mx.corp.example; dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "Subject: Crafted Header With Spoofed AuthservId\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.corp.example");

        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.Equal(AuthenticationProvenance.AmbiguousOrForgedHeader, report.Provenance);
        Assert.Contains(report.PoisonSignals, s => s.Contains("without any Received MTA transit hops", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ConfiguredOptionsTrustedAuthservId_ElevatesProvenanceWithoutExplicitArgument()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new UnsubscribeForensicsOptions
        {
            TrustedAuthservId = "mx.enterprise-gateway.net"
        });
        var configuredAnalyzer = new UnsubscribeForensicAnalyzer(options);

        var eml =
            "Received: from relay.net by mx.enterprise-gateway.net;\r\n" +
            "Authentication-Results: mx.enterprise-gateway.net; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "Subject: Configured Analyzer Ingestion\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = configuredAnalyzer.Analyze(ParseRawEml(eml));

        Assert.Equal(AuthenticationProvenance.TrustedReceivingMx, report.Provenance);
        Assert.True(report.AuthservIdVerified);
        Assert.Equal("mx.enterprise-gateway.net", report.MatchedAuthservId);
    }

    [Fact]
    public void Analyze_ConfiguredOptionsTrustedAuthservId_OverridesPerCallParameterAndFailsClosedWhenHeadersMatchCallerId()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new UnsubscribeForensicsOptions
        {
            TrustedAuthservId = "mx.operator-pinned.internal"
        });
        var configuredAnalyzer = new UnsubscribeForensicAnalyzer(options);

        // Caller attempts to pass client-spoofed authservId, and message headers match that client ID
        var eml =
            "Received: from relay.net by mx.client-spoofed.example;\r\n" +
            "Authentication-Results: mx.client-spoofed.example; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Client Spoofed AuthservId Ingestion\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = configuredAnalyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.client-spoofed.example");

        // Operator configuration takes strict precedence: ExpectedAuthservId is operator pinned, not caller supplied
        Assert.Equal("mx.operator-pinned.internal", report.ExpectedAuthservId);
        Assert.False(report.AuthservIdVerified);
        Assert.Equal(AuthenticationProvenance.UnverifiedUpload, report.Provenance);
        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.Contains(report.PoisonSignals, s => s.Contains("does not match expected trusted authserv-id 'mx.operator-pinned.internal'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ForwardedInnerMessageWithForgedTrustedAuthservId_DoesNotElevateProvenance()
    {
        var innerEml =
            "Received: from relay.net by mx.corp.example;\r\n" +
            "Authentication-Results: mx.corp.example; dkim=pass header.d=systems-digest.io header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: victim@corp.example\r\n" +
            "Subject: Forged Inner Forwarded Digest\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Inner body.";

        var innerMsg = ParseRawEml(innerEml);

        var outer = new MimeMessage();
        outer.From.Add(new MailboxAddress("Forwarder", "forwarder@external.example"));
        outer.To.Add(new MailboxAddress("Analyst", "analyst@corp.example"));
        outer.Subject = "Fwd: check this email";
        outer.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Forwarded message." },
            new MessagePart { Message = innerMsg }
        };

        // Even though inner carries mx.corp.example, the outer wrapper means the trusted MX did not authenticate the inner part
        var report = _analyzer.Analyze(outer, trustedAuthservId: "mx.corp.example");

        Assert.Equal(AuthenticationProvenance.UnverifiedUpload, report.Provenance);
        Assert.False(report.AuthservIdVerified);
    }

    [Fact]
    public void Analyze_TopmostReceivedHopContradictsTrustedAuthservId_RefusesElevationAndFlagsPoison()
    {
        // Topmost Authentication-Results claims mx.corp.example, but topmost Received was processed by mx.attacker-relay.net
        var eml =
            "Received: from mail.systems-digest.io by mx.attacker-relay.net;\r\n" +
            "Authentication-Results: mx.corp.example; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Contradictory Hop Assertion\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Content.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.corp.example");

        Assert.Equal(AuthenticationProvenance.UnverifiedUpload, report.Provenance);
        Assert.False(report.AuthservIdVerified);
        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.Contains(report.PoisonSignals, s => s.Contains("contradicts expected trusted authserv-id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TopmostReceivedHopLacksByClause_FailsClosedAsUnverifiedUploadWithPoison()
    {
        // Topmost Received has no 'by' clause to corroborate the receiving MTA boundary;
        // even if a subsequent deeper hop names the trusted MX, it must stop and fail closed.
        var eml =
            "Received: from mail.systems-digest.io (unknown [192.0.2.1]);\r\n" +
            "Received: from relay.internal by mx.corp.example with ESMTPS id b1;\r\n" +
            "Authentication-Results: mx.corp.example; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Missing By Clause Hop\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Content.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.corp.example");

        Assert.Equal(AuthenticationProvenance.UnverifiedUpload, report.Provenance);
        Assert.False(report.AuthservIdVerified);
        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.Contains(report.PoisonSignals, s => s.Contains("lacks a corroborating 'by' hostname", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_WithTopmostInternalIpv6HopAboveTrustedMx_CorroboratesAndElevatesProvenance()
    {
        var eml =
            "Received: by 2002:a05:6a20:1234:b0:1f0:abc:def with SMTP id q123;\r\n" +
            "Received: from mail.systems-digest.io (mail.systems-digest.io [198.51.100.1]) by mx.google.com with ESMTPS id g456;\r\n" +
            "Authentication-Results: mx.google.com; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@gmail.com\r\n" +
            "Subject: Real Provider Delivery Chain\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Real world provider test.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.google.com");

        Assert.Equal(UnsubscribeClassification.OneClickPostCandidate, report.Classification);
        Assert.Equal(AuthenticationProvenance.TrustedReceivingMx, report.Provenance);
        Assert.True(report.AuthservIdVerified);
        Assert.Equal("mx.google.com", report.MatchedAuthservId);
        Assert.Equal(10, report.RiskScore);
        Assert.Contains("headers name expected receiving MX 'mx.google.com'", report.SummaryVerdict);
    }

    [Fact]
    public void Analyze_WithHexPrefixHostnameInByClause_DoesNotTruncateAndCorroboratesSuccessfully()
    {
        var eml =
            "Received: from mail.systems-digest.io by edge.corp.example with ESMTPS id e123;\r\n" +
            "Authentication-Results: edge.corp.example; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Hex Prefix Hostname Test\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Content.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "edge.corp.example");

        Assert.Equal(AuthenticationProvenance.TrustedReceivingMx, report.Provenance);
        Assert.True(report.AuthservIdVerified);
        Assert.Equal("edge.corp.example", report.MatchedAuthservId);
        Assert.Empty(report.PoisonSignals);
    }

    [Fact]
    public void Analyze_TopmostNonInternalHopContradictsTrustedAuthservId_FailsClosedEvenIfSubsequentHopMatches()
    {
        // Topmost non-internal hop is an attacker relay; a subsequent (deeper) hop mentions the trusted MX.
        // The analyzer must fail closed on the first non-internal hop and never look deeper for a match.
        var eml =
            "Received: from relay.internal by mx.attacker-relay.net with ESMTPS id a1;\r\n" +
            "Received: from mail.systems-digest.io by mx.corp.example with ESMTPS id b2;\r\n" +
            "Authentication-Results: mx.corp.example; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Deeper Hop Spoof Attempt\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Content.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.corp.example");

        Assert.Equal(AuthenticationProvenance.UnverifiedUpload, report.Provenance);
        Assert.False(report.AuthservIdVerified);
        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
        Assert.Contains(report.PoisonSignals, s => s.Contains("('by mx.attacker-relay.net') contradicts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_MatchingAuthservIdWithDuplicateListUnsubscribe_DowngradesToAmbiguousAndSetsAuthservIdVerifiedFalse()
    {
        // Headers initially corroborate expected MX, but duplicate List-Unsubscribe headers downgrade provenance
        var eml =
            "Received: from relay.net by mx.corp.example;\r\n" +
            "Authentication-Results: mx.corp.example; dkim=pass header.d=systems-digest.io header.s=s1; spf=pass\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=systems-digest.io; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJjZA==\r\n" +
            "From: Digest <digest@systems-digest.io>\r\n" +
            "To: user@corp.example\r\n" +
            "Subject: Duplicate List Unsubscribe Attack\r\n" +
            "List-Unsubscribe: <https://optout.systems-digest.io/one-click>\r\n" +
            "List-Unsubscribe: <https://attacker.example/hijack>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n\r\n" +
            "Body.";

        var report = _analyzer.Analyze(ParseRawEml(eml), trustedAuthservId: "mx.corp.example");

        // AuthservIdVerified must be false because Provenance was downgraded to AmbiguousOrForgedHeader
        Assert.Equal(AuthenticationProvenance.AmbiguousOrForgedHeader, report.Provenance);
        Assert.False(report.AuthservIdVerified);
        Assert.Equal(UnsubscribeClassification.PoisonOrReconTrap, report.Classification);
    }
}
