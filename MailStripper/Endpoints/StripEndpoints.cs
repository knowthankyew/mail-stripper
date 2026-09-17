using MailStripper.Models;
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

            try
            {
                using var stream = file.OpenReadStream();
                var result = await parser.ParseStreamAsync(stream, file.FileName);
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
        group.MapPost("/text", async (StripTextRequest request, IEmailParser parser) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Content))
            {
                return Results.BadRequest(new { error = "Content cannot be empty." });
            }

            try
            {
                var result = await parser.ParseTextAsync(request.Content);
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
                version = "1.0.0",
                runtime = ".NET 10.0",
                parser = "MimeKit 4.18.0 (jstedfast)",
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
}
