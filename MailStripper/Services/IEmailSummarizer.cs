namespace MailStripper.Services;

public record SummaryResult(
    string OneSentenceTitle,
    string ShortSummary,
    List<string> KeyBulletPoints,
    string EngineName
);

public interface IEmailSummarizer
{
    Task<SummaryResult> SummarizeAsync(
        string subject,
        string sender,
        string bodyText,
        IReadOnlyList<string> attachmentNames,
        CancellationToken ct = default
    );
}
