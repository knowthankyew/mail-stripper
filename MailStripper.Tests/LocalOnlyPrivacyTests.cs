using MailStripper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailStripper.Tests;

public class LocalOnlyPrivacyTests
{
    [Fact]
    public void PrivacyAudit_VerifiesAirGappedAndZeroDiskWrites()
    {
        var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var auditService = new PrivacyAuditService(store, summarizer);

        var report = auditService.GenerateReport();

        Assert.NotNull(report);
        Assert.True(report.IsAirGapped);
        Assert.Equal(0, report.ExternalConnectionsCount);
        Assert.True(report.ZeroDiskWritesVerified);
        Assert.Contains("default-src 'self'", report.ContentSecurityPolicy);
        Assert.Contains("Zero external pings", report.TelemetryStatus);
        Assert.NotEmpty(report.CryptographicSignature);
    }

    [Fact]
    public async Task ProcessingEmail_NeverWritesToTempDisk()
    {
        var tempDir = Path.GetTempPath();
        var initialFiles = Directory.GetFiles(tempDir, "mailStripper*");

        var store = new MemoryAttachmentStore();
        var summarizer = new ExtractiveEmailSummarizer();
        var parser = new MimeKitEmailParser(summarizer, store, NullLogger<MimeKitEmailParser>.Instance);

        var sampleEml = SampleEmailService.GenerateSampleEml();
        using var stream = new MemoryStream(sampleEml);

        var result = await parser.ParseStreamAsync(stream, "privacy_test.eml");

        // Verify attachments stored in memory
        Assert.True(store.HasAttachments(result.Id));

        // Generate zip in memory
        var zipBytes = store.CreateZipArchive(result.Id, "test");
        Assert.NotNull(zipBytes);
        Assert.True(zipBytes.Length > 0);

        // Verify zero temp files created by mailStripper
        var finalFiles = Directory.GetFiles(tempDir, "mailStripper*");
        Assert.Equal(initialFiles.Length, finalFiles.Length);
    }
}
