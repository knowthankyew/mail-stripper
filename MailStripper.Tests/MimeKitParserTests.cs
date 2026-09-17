using System.IO.Compression;
using System.Text;
using MailStripper.Models;
using MailStripper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailStripper.Tests;

public class MimeKitParserTests
{
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
}
