using System.IO.Compression;
using System.Text;
using MailStripper.Models;
using MailStripper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailStripper.Tests;

public class MimeKitParserTests
{
    static MimeKitParserTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public async Task ParseStreamAsync_ExtractsMetadataAndAttachments_Correctly()
    {
        var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var sampleBytes = SampleEmailService.GenerateSampleEml();
        using var ms = new MemoryStream(sampleBytes);

        var result = await parser.ParseStreamAsync(ms, "test_email.eml");

        Assert.NotNull(result);
        Assert.Equal("Q3 Distributed Pipeline Architecture & Budget Sign-Off", result.Subject);
        Assert.Contains("Cassian Brooks", result.From);
        Assert.Equal(3, result.AttachmentCount);

        var fileNames = result.Attachments.Select(a => a.FileName).ToList();
        Assert.Contains("q3_budget_breakdown.csv", fileNames);
        Assert.Contains("security_compliance_signoff.txt", fileNames);
        Assert.Contains("pipeline_architecture_diagram.png", fileNames);

        Assert.NotEmpty(result.OneSentenceTitle);
        Assert.NotEmpty(result.ShortSummary);
        Assert.True(result.TotalAttachmentBytes > 0);
        Assert.NotEmpty(result.DownloadAllZipUrl);
        Assert.Equal(32, result.Id.Length);

        // Verify attachment store contains items
        var att0 = store.GetAttachment(result.Id, 0);
        Assert.NotNull(att0);
        Assert.NotEmpty(att0.Data);
    }

    [Fact]
    public void CreateZipArchive_ProducesValidZipBundle()
    {
        var store = new MemoryAttachmentStore();
        var sessionId = "test_session_123";

        var attachments = new List<AttachmentData>
        {
            new AttachmentData { Index = 0, Id = "0", FileName = "file1.txt", ContentType = "text/plain", Data = Encoding.UTF8.GetBytes("Content 1") },
            new AttachmentData { Index = 1, Id = "1", FileName = "file2.csv", ContentType = "text/csv", Data = Encoding.UTF8.GetBytes("a,b,c\n1,2,3") }
        };

        store.Store(sessionId, attachments);
        var zipBytes = store.CreateZipArchive(sessionId, "test_archive");

        Assert.NotNull(zipBytes);
        Assert.True(zipBytes.Length > 0);

        // Verify reading back the ZIP archive
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        Assert.Equal(2, archive.Entries.Count);
        Assert.NotNull(archive.GetEntry("file1.txt"));
        Assert.NotNull(archive.GetEntry("file2.csv"));

        using var entry1 = archive.GetEntry("file1.txt")!.Open();
        using var reader = new StreamReader(entry1);
        Assert.Equal("Content 1", reader.ReadToEnd());
    }

    [Fact]
    public async Task ParseTextAsync_HandlesRawPastedSnippet()
    {
        var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var raw = "Please review the attached release notes for sprint 42. Deployment is scheduled for midnight.";
        var result = await parser.ParseTextAsync(raw);

        Assert.NotNull(result);
        Assert.Contains("Please review the attached release notes", result.Subject);
        Assert.NotEmpty(result.OneSentenceTitle);
        Assert.Equal(0, result.AttachmentCount);
    }

    [Fact]
    public async Task ParseStreamAsync_HtmlOnlyEmail_UsesGeneratedRegexToExtractPlainText()
    {
        using var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var eml = "From: sender@example.com\r\n" +
                  "To: recipient@example.com\r\n" +
                  "Subject: HTML Newsletter Update\r\n" +
                  "Content-Type: text/html; charset=utf-8\r\n\r\n" +
                  "<html><head><style>body { color: red; }</style><script>alert('xss');</script></head>" +
                  "<body><h1>Quarterly Performance</h1><p>We achieved <b>140%</b> of target quota.</p><br/>" +
                  "<p>Next sync is tomorrow at 10 AM.</p></body></html>";

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(eml));
        var result = await parser.ParseStreamAsync(ms, "newsletter.eml");

        Assert.NotNull(result);
        Assert.Contains("Quarterly Performance", result.ShortSummary);
        Assert.DoesNotContain("alert", result.ShortSummary);
        Assert.DoesNotContain("color: red", result.ShortSummary);
    }

    [Fact]
    public void MemoryAttachmentStore_Dispose_CleansUpTimerSafely()
    {
        var store = new MemoryAttachmentStore();
        var sessionId = "test_cleanup_session";
        store.Store(sessionId, new List<AttachmentData>
        {
            new AttachmentData { Index = 0, Id = "0", FileName = "file.txt", ContentType = "text/plain", Data = [1, 2, 3] }
        });

        Assert.True(store.HasAttachments(sessionId));
        store.Dispose(); // Should terminate background loop cleanly without throw
    }

    [Fact]
    public async Task ParseStreamAsync_Iso885918BitBody_PreservesAccentedCharactersWithoutReplacementChar()
    {
        using var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var rawMessage =
            "From: Chef <chef@gastronomie.fr>\r\n" +
            "To: gourmet@gastronomie.fr\r\n" +
            "Subject: =?ISO-8859-1?Q?Menu_d=E9gustation_d'=E9t=E9?=\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/plain; charset=iso-8859-1\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n\r\n" +
            "Voici notre crème brûlée, café liégeois, et pâté de campagne pour Herr Müller.";

        var rawBytes = latin1.GetBytes(rawMessage);
        using var ms = new MemoryStream(rawBytes);

        var result = await parser.ParseStreamAsync(ms, "menu_iso88591.eml");

        Assert.NotNull(result);
        Assert.Equal("Menu dégustation d'été", result.Subject);
        Assert.Contains("crème brûlée", result.BodyText);
        Assert.Contains("café liégeois", result.BodyText);
        Assert.Contains("pâté de campagne", result.BodyText);
        Assert.Contains("Herr Müller", result.BodyText);
        Assert.DoesNotContain("\uFFFD", result.BodyText);
        Assert.DoesNotContain("\uFFFD", result.Subject);
    }

    [Fact]
    public async Task ParseStreamAsync_Windows12528BitBody_PreservesExtendedPunctuationAndCurrency()
    {
        using var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var win1252 = Encoding.GetEncoding(1252);
        var rawMessage =
            "From: Finance <billing@enterprise.corp>\r\n" +
            "To: vendor@enterprise.corp\r\n" +
            "Subject: Invoice Sign-Off\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/plain; charset=windows-1252\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n\r\n" +
            "Total Approved: 1,500 € — “Expedited Project”™ milestone (discount: 50 € – 100 €).";

        var rawBytes = win1252.GetBytes(rawMessage);
        using var ms = new MemoryStream(rawBytes);

        var result = await parser.ParseStreamAsync(ms, "invoice_win1252.eml");

        Assert.NotNull(result);
        Assert.Contains("1,500 €", result.BodyText);
        Assert.Contains("“Expedited Project”™", result.BodyText);
        Assert.Contains("50 € – 100 €", result.BodyText);
        Assert.DoesNotContain("\uFFFD", result.BodyText);
    }

    [Fact]
    public async Task ParseStreamAsync_Iso88591Newsletter_ForensicAnalyzerDecodesAccentedAnchorAndRfc8058WithoutCorruption()
    {
        using var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var rawMessage =
            "Received: from mta.bulletin-pro.fr by mx.local;\r\n" +
            "Authentication-Results: mx.local; dkim=pass header.d=bulletin-pro.fr header.s=s1\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; d=bulletin-pro.fr; s=s1; h=from:list-unsubscribe:list-unsubscribe-post; b=YWJj\r\n" +
            "From: Bulletin Pro <redaction@bulletin-pro.fr>\r\n" +
            "To: abonne@bulletin-pro.fr\r\n" +
            "Subject: =?ISO-8859-1?Q?Actualit=E9s_de_la_semaine?=\r\n" +
            "List-Unsubscribe: <https://bulletin-pro.fr/desabonner-oneclick>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/html; charset=iso-8859-1\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n\r\n" +
            "<html><body><p>Actualités hebdomadaires.</p><p><a href=\"https://bulletin-pro.fr/desabonner\">Désabonnement sécurisé</a></p></body></html>";

        var rawBytes = latin1.GetBytes(rawMessage);
        using var ms = new MemoryStream(rawBytes);

        var result = await parser.ParseStreamAsync(ms, "bulletin_iso88591.eml");

        Assert.NotNull(result);
        Assert.Equal("Actualités de la semaine", result.Subject);
        Assert.Equal(UnsubscribeClassification.OneClickPostCandidate, result.Unsubscribe.Classification);
        Assert.DoesNotContain("\uFFFD", result.BodyText);

        var htmlAnchorCandidate = result.Unsubscribe.Candidates.FirstOrDefault(c => c.Source == "BodyHtmlAnchor");
        Assert.NotNull(htmlAnchorCandidate);
        Assert.Equal("Désabonnement sécurisé", htmlAnchorCandidate.AnchorText);
        Assert.DoesNotContain("\uFFFD", htmlAnchorCandidate.AnchorText);
    }

    [Fact]
    public async Task ParseStreamAsync_RawBinaryAttachmentStream_PreservesExactBytes()
    {
        using var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        // Generate binary payload covering all 256 byte values 0x00..0xFF
        var binaryPayload = new byte[1024];
        for (int i = 0; i < binaryPayload.Length; i++)
        {
            binaryPayload[i] = (byte)(i % 256);
        }

        var base64Attachment = Convert.ToBase64String(binaryPayload);
        var rawMessage =
            "From: Backup <backup@system.local>\r\n" +
            "To: admin@system.local\r\n" +
            "Subject: Daily Forensic Snapshot\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=\"====BOUNDARY====\"\r\n\r\n" +
            "--====BOUNDARY====\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
            "Attached forensic binary.\r\n" +
            "--====BOUNDARY====\r\n" +
            "Content-Type: application/octet-stream; name=\"snapshot.bin\"\r\n" +
            "Content-Disposition: attachment; filename=\"snapshot.bin\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" +
            base64Attachment + "\r\n" +
            "--====BOUNDARY====--";

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(rawMessage));
        var result = await parser.ParseStreamAsync(ms, "forensic_backup.eml");

        Assert.NotNull(result);
        Assert.Equal(1, result.AttachmentCount);

        var storedAttachment = store.GetAttachment(result.Id, 0);
        Assert.NotNull(storedAttachment);
        Assert.Equal("snapshot.bin", storedAttachment.FileName);
        Assert.Equal(binaryPayload.Length, storedAttachment.Data.Length);
        Assert.Equal(binaryPayload, storedAttachment.Data);
    }
}
