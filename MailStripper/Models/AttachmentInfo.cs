namespace MailStripper.Models;

public class AttachmentInfo
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = "attachment.bin";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string FormattedSize { get; set; } = "0 B";
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string? PreviewUrl { get; set; }
    public bool IsPreviewable { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public string Category { get; set; } = "generic"; // document, image, spreadsheet, archive, code, generic
}

public class AttachmentData
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = "attachment.bin";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
}
