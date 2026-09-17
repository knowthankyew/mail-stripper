using System.Text;
using System.Text.RegularExpressions;
using MailStripper.Models;
using MimeKit;

namespace MailStripper.Services;

public partial class MimeKitEmailParser : IEmailParser
{
    private readonly IEmailSummarizer _summarizer;
    private readonly IAttachmentStore _attachmentStore;
    private readonly ILogger<MimeKitEmailParser> _logger;

    public MimeKitEmailParser(
        IEmailSummarizer summarizer,
        IAttachmentStore attachmentStore,
        ILogger<MimeKitEmailParser> logger)
    {
        _summarizer = summarizer;
        _attachmentStore = attachmentStore;
        _logger = logger;
    }

    public async Task<StrippedEmail> ParseStreamAsync(Stream stream, string? originalFileName = null, CancellationToken ct = default)
    {
        // 1. Rigorous file format and binary content validation
        var validation = EmailValidator.Validate(stream, originalFileName);
        if (!validation.IsValid)
        {
            throw new InvalidEmailException(validation.ErrorMessage ?? "Invalid email file format.", validation.DetectedType);
        }

        var parser = new MimeParser(stream, MimeFormat.Default);
        MimeMessage message;
        try
        {
            message = await parser.ParseMessageAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MimeParser failed for file {FileName}", originalFileName);
            throw new InvalidEmailException(
                $"Unable to parse '{originalFileName ?? "uploaded file"}' as a valid RFC 822 email message: {ex.Message}",
                "corrupted_mime"
            );
        }

        // 2. Validate that the parsed MIME message has minimum email semantic structure
        ValidateMimeSemantics(message, originalFileName);

        return await ProcessMimeMessageAsync(message, originalFileName, ct);
    }

    private static void ValidateMimeSemantics(MimeMessage message, string? originalFileName)
    {
        var hasHeaders = message.Headers.Count > 0;
        var hasSender = message.From.Count > 0 || message.Sender != null;
        var hasSubject = !string.IsNullOrWhiteSpace(message.Subject);
        var hasBody = !string.IsNullOrWhiteSpace(message.TextBody) || !string.IsNullOrWhiteSpace(message.HtmlBody);
        var hasAttachments = message.BodyParts.Any(p => p.IsAttachment || p is MessagePart);

        // Check for binary corruption in subject or headers
        if (hasSubject && message.Subject != null && message.Subject.Take(20).Any(c => char.IsControl(c) && c != '\t' && c != '\r' && c != '\n'))
        {
            throw new InvalidEmailException(
                $"The file '{originalFileName ?? "uploaded file"}' contains non-text binary control codes in its headers. It appears to be a binary file, not an email message.",
                "binary_corruption"
            );
        }

        if (!hasSender && !hasSubject && !hasBody && !hasAttachments)
        {
            throw new InvalidEmailException(
                $"The file '{originalFileName ?? "uploaded file"}' does not contain recognized email headers (From, Subject, To), body text, or attachments. Please upload an exported email file (.eml).",
                "empty_or_non_email"
            );
        }
    }

    public async Task<StrippedEmail> ParseTextAsync(string rawContent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return CreateEmptyStrippedEmail();
        }

        // Check if rawContent looks like an RFC822 message (contains headers like From: / Subject: followed by double newline)
        var hasHeaders = LooksLikeRfc822(rawContent);

        byte[] bytes;
        if (hasHeaders)
        {
            bytes = Encoding.UTF8.GetBytes(rawContent);
        }
        else
        {
            // Wrap in synthetic RFC 822 format so MimeKit parses it with full fidelity
            var firstLine = rawContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Pasted Message";
            var syntheticSubject = firstLine.Length > 60 ? firstLine[..57] + "..." : firstLine;
            var syntheticHeader = $"From: Pasted Input <clipboard@local>\r\nSubject: {syntheticSubject}\r\nDate: {DateTimeOffset.UtcNow:R}\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n";
            bytes = Encoding.UTF8.GetBytes(syntheticHeader + rawContent);
        }

        using var ms = new MemoryStream(bytes);
        return await ParseStreamAsync(ms, "pasted_email.eml", ct);
    }

    private async Task<StrippedEmail> ProcessMimeMessageAsync(MimeMessage message, string? originalFileName, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("n");
        var rawSubject = message.Subject ?? string.Empty;
        var cleanSubject = ExtractiveEmailSummarizer.CleanSubject(rawSubject);
        if (string.IsNullOrWhiteSpace(cleanSubject) && !string.IsNullOrWhiteSpace(originalFileName))
        {
            cleanSubject = Path.GetFileNameWithoutExtension(originalFileName);
        }

        var sender = message.From.ToString();
        if (string.IsNullOrWhiteSpace(sender))
        {
            sender = message.Sender?.ToString() ?? "(Unknown Sender)";
        }

        var toList = message.To.Select(t => t.ToString()).ToList();
        var ccList = message.Cc.Select(c => c.ToString()).ToList();

        var bodyText = message.TextBody ?? string.Empty;
        var bodyHtml = message.HtmlBody;

        // If text body is empty but HTML is available, extract plain text from HTML
        if (string.IsNullOrWhiteSpace(bodyText) && !string.IsNullOrWhiteSpace(bodyHtml))
        {
            bodyText = HtmlToPlainText(bodyHtml);
        }

        // Extract attachments
        var extractedData = new List<AttachmentData>();
        var attachmentInfos = new List<AttachmentInfo>();
        int index = 0;

        foreach (var entity in message.BodyParts)
        {
            if (entity.IsAttachment || entity is MessagePart || !string.IsNullOrEmpty(entity.ContentDisposition?.FileName))
            {
                var attData = ExtractAttachmentData(entity, index, sessionId);
                if (attData != null)
                {
                    extractedData.Add(attData);

                    var ext = Path.GetExtension(attData.FileName).ToLowerInvariant().TrimStart('.');
                    var info = new AttachmentInfo
                    {
                        Index = index,
                        Id = attData.Id,
                        FileName = attData.FileName,
                        ContentType = attData.ContentType,
                        SizeBytes = attData.Data.Length,
                        FormattedSize = FormatBytes(attData.Data.Length),
                        IsInline = attData.IsInline,
                        ContentId = attData.ContentId,
                        FileExtension = ext,
                        Category = DetermineCategory(ext, attData.ContentType),
                        DownloadUrl = $"/api/strip/{sessionId}/attachments/{index}",
                        PreviewUrl = $"/api/strip/{sessionId}/attachments/{index}/preview",
                        IsPreviewable = IsPreviewable(ext, attData.ContentType)
                    };

                    attachmentInfos.Add(info);
                    index++;
                }
            }
        }

        // Store attachments in session store
        if (extractedData.Count > 0)
        {
            _attachmentStore.Store(sessionId, extractedData);
        }

        // Summarize
        var attachmentNames = attachmentInfos.Select(a => a.FileName).ToList();
        var summary = await _summarizer.SummarizeAsync(cleanSubject, sender, bodyText, attachmentNames, ct);

        var totalBytes = attachmentInfos.Sum(a => a.SizeBytes);
        var wordCount = bodyText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        return new StrippedEmail
        {
            Id = sessionId,
            Subject = cleanSubject,
            RawSubject = rawSubject,
            From = sender,
            To = toList,
            Cc = ccList,
            Date = message.Date,
            FormattedDate = message.Date != default ? message.Date.ToString("f") : DateTimeOffset.UtcNow.ToString("f"),
            MessageId = message.MessageId,
            OneSentenceTitle = summary.OneSentenceTitle,
            ShortSummary = summary.ShortSummary,
            KeyBulletPoints = summary.KeyBulletPoints,
            SummaryEngine = summary.EngineName,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            HasHtml = !string.IsNullOrWhiteSpace(bodyHtml),
            WordCount = wordCount,
            Attachments = attachmentInfos,
            TotalAttachmentBytes = totalBytes,
            TotalAttachmentSizeFormatted = FormatBytes(totalBytes),
            DownloadAllZipUrl = attachmentInfos.Count > 0 ? $"/api/strip/{sessionId}/attachments/download-all" : string.Empty
        };
    }

    private static AttachmentData? ExtractAttachmentData(MimeEntity entity, int index, string sessionId)
    {
        var id = $"{sessionId}_{index}";

        if (entity is MessagePart messagePart)
        {
            if (messagePart.Message == null) return null;
            var fileName = messagePart.ContentDisposition?.FileName ?? $"attached_message_{index + 1}.eml";
            using var ms = new MemoryStream();
            messagePart.Message.WriteTo(ms);
            return new AttachmentData
            {
                Index = index,
                Id = id,
                FileName = fileName,
                ContentType = "message/rfc822",
                Data = ms.ToArray(),
                IsInline = false
            };
        }

        if (entity is MimePart mimePart)
        {
            if (mimePart.Content == null) return null;
            var fileName = mimePart.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                var sub = mimePart.ContentType.MediaSubtype.ToLowerInvariant();
                fileName = $"attachment_{index + 1}.{sub}";
            }

            using var ms = new MemoryStream();
            mimePart.Content.DecodeTo(ms);

            var isInline = mimePart.ContentDisposition?.Disposition == ContentDisposition.Inline ||
                           !string.IsNullOrEmpty(mimePart.ContentId);

            return new AttachmentData
            {
                Index = index,
                Id = id,
                FileName = fileName,
                ContentType = mimePart.ContentType.MimeType,
                Data = ms.ToArray(),
                IsInline = isInline,
                ContentId = mimePart.ContentId
            };
        }

        return null;
    }

    private static bool LooksLikeRfc822(string text)
    {
        var firstLines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(15);
        int headerCount = 0;
        foreach (var line in firstLines)
        {
            if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("To:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            {
                headerCount++;
            }
        }
        return headerCount >= 2;
    }

    private static string HtmlToPlainText(string html)
    {
        var text = StyleRegex().Replace(html, "");
        text = ScriptRegex().Replace(text, "");
        text = BrRegex().Replace(text, "\n");
        text = ParagraphRegex().Replace(text, "\n\n");
        text = HtmlTagRegex().Replace(text, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return string.Join("\n", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
    }

    private static string DetermineCategory(string ext, string contentType)
    {
        var c = contentType.ToLowerInvariant();
        if (c.StartsWith("image/") || ext is "png" or "jpg" or "jpeg" or "gif" or "webp" or "svg") return "image";
        if (c == "application/pdf" || ext is "pdf") return "document";
        if (ext is "csv" or "xlsx" or "xls" or "tsv") return "spreadsheet";
        if (ext is "zip" or "tar" or "gz" or "7z" or "rar") return "archive";
        if (ext is "cs" or "py" or "js" or "ts" or "json" or "html" or "xml" or "sql") return "code";
        if (ext is "doc" or "docx" or "txt" or "md" or "rtf") return "document";
        return "generic";
    }

    private static bool IsPreviewable(string ext, string contentType)
    {
        var c = contentType.ToLowerInvariant();
        return c.StartsWith("image/") ||
               c == "application/pdf" ||
               c.StartsWith("text/") ||
               ext is "png" or "jpg" or "jpeg" or "gif" or "webp" or "svg" or "pdf" or "txt" or "json" or "csv" or "md";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    private static StrippedEmail CreateEmptyStrippedEmail()
    {
        return new StrippedEmail
        {
            Subject = "Empty Email",
            From = "None",
            OneSentenceTitle = "No email content provided to strip.",
            ShortSummary = "The input stream or pasted text was empty.",
            SummaryEngine = "None"
        };
    }

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"</p>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
