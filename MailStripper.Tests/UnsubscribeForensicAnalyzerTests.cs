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
}
