using MailStripper.Models;

namespace MailStripper.Services;

public interface IEmailParser
{
    Task<StrippedEmail> ParseStreamAsync(Stream stream, string? originalFileName = null, CancellationToken ct = default);
    Task<StrippedEmail> ParseStreamAsync(Stream stream, string? originalFileName, string? trustedAuthservId, CancellationToken ct = default);
    Task<StrippedEmail> ParseTextAsync(string rawContent, CancellationToken ct = default);
    Task<StrippedEmail> ParseTextAsync(string rawContent, string? trustedAuthservId, CancellationToken ct = default);
}
