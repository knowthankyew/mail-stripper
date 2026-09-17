namespace MailStripper.Models;

public class StrippedEmail
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public string Subject { get; set; } = "(No Subject)";
    public string RawSubject { get; set; } = string.Empty;
    public string From { get; set; } = "(Unknown Sender)";
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public DateTimeOffset? Date { get; set; }
    public string FormattedDate { get; set; } = string.Empty;
    public string? MessageId { get; set; }
    
    // Summarization Outputs
    public string OneSentenceTitle { get; set; } = string.Empty;
    public string ShortSummary { get; set; } = string.Empty;
    public List<string> KeyBulletPoints { get; set; } = new();
    public string SummaryEngine { get; set; } = "Smart Extractive (MimeKit)";

    // Cleaned bodies
    public string BodyText { get; set; } = string.Empty;
    public string? BodyHtml { get; set; }
    public bool HasHtml { get; set; }
    public int WordCount { get; set; }

    // Stripped Attachments
    public List<AttachmentInfo> Attachments { get; set; } = new();
    public int AttachmentCount => Attachments.Count;
    public long TotalAttachmentBytes { get; set; }
    public string TotalAttachmentSizeFormatted { get; set; } = "0 B";
    public string DownloadAllZipUrl { get; set; } = string.Empty;
}

public class StripTextRequest
{
    public string Content { get; set; } = string.Empty;
}
