using System.Runtime.InteropServices;
using MailStripper.Models;
using MailStripper.Security;
using MailStripper.Services;

namespace MailStripper.Endpoints;

public static class StripEndpoints
{
    public static void MapStripEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/strip");

        // Ingest email file (.eml / .msg / .txt)
        group.MapPost("/file", async (HttpRequest request, IEmailParser parser) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Request must be multipart/form-data." });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No email file uploaded or file is empty." });
            }

            if (file.Length > SecurityConstants.MaxUploadSizeBytes)
            {
                return Results.BadRequest(new { error = $"File exceeds {SecurityConstants.MaxUploadSizeBytes / (1024 * 1024)} MB limit." });
            }

            string? formAuthserv = form.TryGetValue("authservId", out var fVal) ? fVal.ToString() : null;
            if (!TryResolveAuthservId(formAuthserv, request, out var authservId, out var authservErr))
            {
                return Results.BadRequest(new { error = authservErr });
            }

            try
            {
                using var stream = file.OpenReadStream();
                var result = await parser.ParseStreamAsync(stream, file.FileName, authservId);
                return Results.Ok(result);
            }
            catch (InvalidEmailException ex)
            {
                return Results.BadRequest(new { error = ex.Message, detectedType = ex.DetectedType });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Failed to parse email file: {ex.Message}" });
            }
        }).DisableAntiforgery();

        // Ingest raw text or RFC822 snippet
        group.MapPost("/text", async (StripTextRequest request, HttpRequest httpRequest, IEmailParser parser) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Content))
            {
                return Results.BadRequest(new { error = "Content cannot be empty." });
            }

            if (request.Content.Length > SecurityConstants.MaxUploadSizeBytes)
            {
                return Results.BadRequest(new { error = $"Content exceeds {SecurityConstants.MaxUploadSizeBytes / (1024 * 1024)} MB limit." });
            }

            if (!TryResolveAuthservId(request.ExpectedAuthservId, httpRequest, out var authservId, out var authservErr))
            {
                return Results.BadRequest(new { error = authservErr });
            }

            try
            {
                var result = await parser.ParseTextAsync(request.Content, authservId);
                return Results.Ok(result);
            }
            catch (InvalidEmailException ex)
            {
                return Results.BadRequest(new { error = ex.Message, detectedType = ex.DetectedType });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Failed to parse pasted text: {ex.Message}" });
            }
        });

        // Download specific attachment
        group.MapGet("/{sessionId}/attachments/{index:int}", (string sessionId, int index, IAttachmentStore store) =>
        {
            var attachment = store.GetAttachment(sessionId, index);
            if (attachment == null)
            {
                return Results.NotFound(new { error = $"Attachment index {index} not found for session {sessionId}." });
            }

            return Results.File(
                fileContents: attachment.Data,
                contentType: string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType,
                fileDownloadName: attachment.FileName
            );
        });

        // Inline preview for images, text, and pdfs
        group.MapGet("/{sessionId}/attachments/{index:int}/preview", (string sessionId, int index, IAttachmentStore store) =>
        {
            var attachment = store.GetAttachment(sessionId, index);
            if (attachment == null)
            {
                return Results.NotFound(new { error = "Attachment not found." });
            }

            return Results.Bytes(
                attachment.Data,
                contentType: string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType
            );
        });

        // Download all attachments bundled in a single ZIP
        group.MapGet("/{sessionId}/attachments/download-all", (string sessionId, IAttachmentStore store) =>
        {
            var zipBytes = store.CreateZipArchive(sessionId, "mailStripper");
            if (zipBytes == null || zipBytes.Length == 0)
            {
                return Results.NotFound(new { error = $"No attachments found for session {sessionId} to archive." });
            }

            var zipName = $"mailStripper_{sessionId}_attachments.zip";
            return Results.File(
                fileContents: zipBytes,
                contentType: "application/zip",
                fileDownloadName: zipName
            );
        });

        // Quick demo: load parsed sample email
        group.MapGet("/sample", async (IEmailParser parser) =>
        {
            var sampleBytes = SampleEmailService.GenerateSampleEml();
            using var ms = new MemoryStream(sampleBytes);
            var result = await parser.ParseStreamAsync(ms, "sample_enterprise_email.eml");
            return Results.Ok(result);
        });

        // Quick demo: load RFC 8058 One-Click newsletter fixture
        group.MapGet("/sample/newsletter", async (IEmailParser parser) =>
        {
            var sampleBytes = SampleEmailService.GenerateSampleNewsletterEml();
            using var ms = new MemoryStream(sampleBytes);
            var result = await parser.ParseStreamAsync(ms, "sample_rfc8058_newsletter.eml");
            return Results.Ok(result);
        });

        // Quick demo: load poisoned phishing unsubscribe trap fixture
        group.MapGet("/sample/poison-trap", async (IEmailParser parser) =>
        {
            var sampleBytes = SampleEmailService.GenerateSamplePoisonTrapEml();
            using var ms = new MemoryStream(sampleBytes);
            var result = await parser.ParseStreamAsync(ms, "sample_poisoned_unsubscribe_trap.eml");
            return Results.Ok(result);
        });

        // Download the sample .eml file directly
        group.MapGet("/sample/download", () =>
        {
            var sampleBytes = SampleEmailService.GenerateSampleEml();
            return Results.File(sampleBytes, "message/rfc822", "sample_enterprise_email.eml");
        });

        // Health & ML Status check
        group.MapGet("/status", async (IEmailSummarizer summarizer) =>
        {
            bool mlOnline = false;
            if (summarizer is MlHybridSummarizer hybrid)
            {
                mlOnline = await hybrid.IsMlAvailableAsync();
            }

            return Results.Ok(new
            {
                service = "mailStripper",
                version = typeof(StripEndpoints).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                runtime = RuntimeInformation.FrameworkDescription,
                parser = "MimeKit (jstedfast)",
                mlInferenceStatus = mlOnline ? "Online (SmolLM2-135M)" : "Offline (Using Smart Extractive)",
                mlInferenceOnline = mlOnline,
                airGappedLocalOnly = true
            });
        });

        // Cryptographic & Live Privacy Audit verification endpoint
        group.MapGet("/privacy-audit", (PrivacyAuditService auditService) =>
        {
            var report = auditService.GenerateReport();
            return Results.Ok(report);
        });
    }

    internal static bool TryResolveAuthservId(string? primary, HttpRequest req, out string? id, out string? error)
    {
        string? raw = !string.IsNullOrWhiteSpace(primary)
            ? primary
            : req.Headers.TryGetValue("X-Expected-Authserv-Id", out var h) && !string.IsNullOrWhiteSpace(h)
                ? h.ToString()
                : req.Query.TryGetValue("authservId", out var q) && !string.IsNullOrWhiteSpace(q)
                    ? q.ToString()
                    : null;

        id = raw?.Trim();
        error = null;
        if (id != null)
        {
            if (id.Length > 255 || id.Any(c => char.IsControl(c) && c != '\t'))
            {
                error = "authservId cannot exceed 255 characters or contain control characters.";
                return false;
            }
        }
        return true;
    }
}
