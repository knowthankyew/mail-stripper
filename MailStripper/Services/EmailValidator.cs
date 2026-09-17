using System.Text;

namespace MailStripper.Services;

public record EmailValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? DetectedType
);

public static class EmailValidator
{
    private static readonly HashSet<string> NonEmailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video / Audio
        ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v",
        ".mp3", ".wav", ".aac", ".flac", ".ogg", ".m4a",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tiff", ".svg",
        // Standalone Documents & Archives
        ".pdf", ".docx", ".xlsx", ".pptx", ".zip", ".tar", ".gz", ".7z", ".rar",
        // Binaries / Executables
        ".exe", ".dmg", ".pkg", ".app", ".iso", ".bin"
    };

    public static EmailValidationResult Validate(Stream stream, string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        // 1. Extension Check
        if (NonEmailExtensions.Contains(ext))
        {
            var friendlyType = GetFriendlyTypeName(ext);
            return new EmailValidationResult(
                IsValid: false,
                ErrorMessage: $"The uploaded file '{fileName}' is a {friendlyType}, not an email message. mailStripper is designed to parse exported emails (.eml) and strip their attachments. Did you upload an attachment directly instead of the email?",
                DetectedType: friendlyType
            );
        }

        // 2. Binary Magic Bytes & Null Byte Inspection
        var originalPos = stream.CanSeek ? stream.Position : 0;
        var buffer = new byte[1024];
        int read = stream.Read(buffer, 0, buffer.Length);
        if (stream.CanSeek)
        {
            stream.Position = originalPos;
        }

        if (read == 0)
        {
            return new EmailValidationResult(
                IsValid: false,
                ErrorMessage: "The uploaded file is empty (0 bytes).",
                DetectedType: "empty"
            );
        }

        // Check for specific binary magic bytes
        var magicResult = CheckMagicBytes(buffer, read, fileName);
        if (!magicResult.IsValid)
        {
            return magicResult;
        }

        // Check for null bytes (\0) in the header probe
        // Standard RFC 822 emails are 7-bit/8-bit text files and never start with null bytes.
        int nullCount = 0;
        for (int i = 0; i < read; i++)
        {
            if (buffer[i] == 0) nullCount++;
        }

        if (nullCount > 0)
        {
            return new EmailValidationResult(
                IsValid: false,
                ErrorMessage: $"The uploaded file '{fileName ?? "input"}' contains raw binary data ({nullCount} null bytes detected in header). It is not a valid RFC 822 email message.",
                DetectedType: "binary"
            );
        }

        return new EmailValidationResult(IsValid: true, ErrorMessage: null, DetectedType: "text/email");
    }

    public static EmailValidationResult ValidatePastedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new EmailValidationResult(
                IsValid: false,
                ErrorMessage: "Pasted email content cannot be empty.",
                DetectedType: "empty"
            );
        }

        if (text.Contains('\0'))
        {
            return new EmailValidationResult(
                IsValid: false,
                ErrorMessage: "Pasted content contains null binary bytes and is not valid text.",
                DetectedType: "binary"
            );
        }

        return new EmailValidationResult(IsValid: true, ErrorMessage: null, DetectedType: "text/email");
    }

    private static EmailValidationResult CheckMagicBytes(byte[] buffer, int len, string? fileName)
    {
        // MP4 signature: bytes 4..7 are 'ftyp'
        if (len >= 8 && buffer[4] == 'f' && buffer[5] == 't' && buffer[6] == 'y' && buffer[7] == 'p')
        {
            return Fail("video (MP4/ISO Media)", fileName);
        }

        // PNG: 89 50 4E 47
        if (len >= 4 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
        {
            return Fail("PNG image", fileName);
        }

        // JPEG: FF D8 FF
        if (len >= 3 && buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
        {
            return Fail("JPEG image", fileName);
        }

        // GIF: GIF8
        if (len >= 4 && buffer[0] == 'G' && buffer[1] == 'I' && buffer[2] == 'F' && buffer[3] == '8')
        {
            return Fail("GIF image", fileName);
        }

        // PDF: %PDF-
        if (len >= 5 && buffer[0] == '%' && buffer[1] == 'P' && buffer[2] == 'D' && buffer[3] == 'F' && buffer[4] == '-')
        {
            return Fail("PDF document", fileName);
        }

        // ZIP / Office docx / xlsx: PK\x03\x04
        if (len >= 4 && buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04)
        {
            return Fail("ZIP archive or Office package", fileName);
        }

        // Mach-O
        if (len >= 4 && (
            (buffer[0] == 0xCF && buffer[1] == 0xFA && buffer[2] == 0xED && buffer[3] == 0xFE) ||
            (buffer[0] == 0xCE && buffer[1] == 0xFA && buffer[2] == 0xED && buffer[3] == 0xFE) ||
            (buffer[0] == 0xCA && buffer[1] == 0xFE && buffer[2] == 0xBA && buffer[3] == 0xBE)))
        {
            return Fail("binary executable (Mach-O)", fileName);
        }

        // ELF: 7F 45 4C 46
        if (len >= 4 && buffer[0] == 0x7F && buffer[1] == 'E' && buffer[2] == 'L' && buffer[3] == 'F')
        {
            return Fail("binary executable (ELF)", fileName);
        }

        // Windows PE: MZ
        if (len >= 2 && buffer[0] == 'M' && buffer[1] == 'Z')
        {
            return Fail("Windows executable / DLL", fileName);
        }

        // OLE Compound File (e.g. Outlook .msg binary): D0 CF 11 E0 A1 B1 1A E1
        if (len >= 8 && buffer[0] == 0xD0 && buffer[1] == 0xCF && buffer[2] == 0x11 && buffer[3] == 0xE0)
        {
            return new EmailValidationResult(
                IsValid: false,
                ErrorMessage: $"The file '{fileName ?? "message.msg"}' is an Outlook binary OLE format (.msg). Please export the email as standard MIME RFC 822 (.eml) or paste its text.",
                DetectedType: "Outlook Binary (.msg)"
            );
        }

        return new EmailValidationResult(IsValid: true, ErrorMessage: null, DetectedType: null);

        static EmailValidationResult Fail(string type, string? name) => new(
            IsValid: false,
            ErrorMessage: $"The file '{name ?? "uploaded file"}' is a {type}, not an email message (.eml). mailStripper requires an exported email file so it can extract attachments and summarize the message.",
            DetectedType: type
        );
    }

    private static string GetFriendlyTypeName(string ext) => ext switch
    {
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".m4v" => "video file",
        ".mp3" or ".wav" or ".aac" or ".flac" or ".ogg" or ".m4a" => "audio file",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "image file",
        ".pdf" => "PDF document",
        ".docx" => "Word document",
        ".xlsx" => "Excel spreadsheet",
        ".pptx" => "PowerPoint presentation",
        ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "compressed archive",
        ".exe" or ".dmg" or ".pkg" or ".app" => "application/installer",
        _ => "binary file"
    };
}
