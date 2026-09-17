using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MailStripper.Services;

public class SummarizerOptions
{
    public string MlEndpoint { get; set; } = "http://localhost:8000/api/v1/inference/generate";
    public string HealthEndpoint { get; set; } = "http://localhost:8000/healthz";
    public bool EnableMl { get; set; } = true;
    public int TimeoutMs { get; set; } = 15000;
}

public class MlHybridSummarizer : IEmailSummarizer
{
    private readonly ExtractiveEmailSummarizer _extractive;
    private readonly HttpClient _httpClient;
    private readonly IOptions<SummarizerOptions> _options;
    private readonly ILogger<MlHybridSummarizer> _logger;

    public MlHybridSummarizer(
        ExtractiveEmailSummarizer extractive,
        HttpClient httpClient,
        IOptions<SummarizerOptions> options,
        ILogger<MlHybridSummarizer> logger)
    {
        _extractive = extractive;
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IsMlAvailableAsync(CancellationToken ct = default)
    {
        if (!_options.Value.EnableMl) return false;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1200));

            var res = await _httpClient.GetAsync(_options.Value.HealthEndpoint, cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<SummaryResult> SummarizeAsync(
        string subject,
        string sender,
        string bodyText,
        IReadOnlyList<string> attachmentNames,
        CancellationToken ct = default)
    {
        // Compute the deterministic extractive summary first as baseline/fallback
        var extractiveResult = await _extractive.SummarizeAsync(subject, sender, bodyText, attachmentNames, ct);

        if (!_options.Value.EnableMl)
        {
            return extractiveResult;
        }

        try
        {
            var senderName = ExtractiveEmailSummarizer.ExtractSenderDisplayName(sender);
            var cleanSubject = ExtractiveEmailSummarizer.CleanSubject(subject);
            var cleanSnippet = ExtractiveEmailSummarizer.GetCleanPlainEnglishSnippet(bodyText, 500);
            var attClause = attachmentNames.Count > 0 ? string.Join(", ", attachmentNames) : "none";

            var prompt = $"Summarize this email in ONE crisp plain English sentence stating what {senderName} needs or communicated:\n" +
                         $"Subject: {cleanSubject}\n" +
                         $"Body: {cleanSnippet}\n" +
                         $"Attachments: {attClause}\n" +
                         $"Summary:";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(_options.Value.TimeoutMs));

            var payload = new MlGenerateRequest(
                Prompt: prompt,
                MaxTokens: 45,
                Temperature: 0.1f
            );

            var response = await _httpClient.PostAsJsonAsync(_options.Value.MlEndpoint, payload, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var mlData = await response.Content.ReadFromJsonAsync<MlGenerateResponse>(cancellationToken: cts.Token);
                if (mlData != null && !string.IsNullOrWhiteSpace(mlData.Completion))
                {
                    var cleanCompletion = CleanMlCompletion(mlData.Completion);
                    if (IsValidPlainEnglishSummary(cleanCompletion, cleanSubject))
                    {
                        return extractiveResult with
                        {
                            OneSentenceTitle = cleanCompletion,
                            EngineName = "SmolLM2-135M (FTaaS ML + MimeKit)"
                        };
                    }
                    else
                    {
                        _logger.LogDebug("ML completion did not meet plain-English criteria ('{Completion}'). Used Smart Extractive title.", cleanCompletion);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "ML Inference endpoint not reachable or timed out. Gracefully used Smart Extractive summarizer.");
        }

        return extractiveResult;
    }

    public static string CleanMlCompletion(string raw)
    {
        var line = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        line = line.Trim().Trim('"', '\'');
        if (line.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
        {
            line = line[8..].Trim();
        }

        // Clean out any stray brackets or quotes
        line = line.Trim('"', '\'', '<', '>', '[', ']', ' ');

        if (!string.IsNullOrWhiteSpace(line) && !line.EndsWith('.') && !line.EndsWith('?') && !line.EndsWith('!'))
        {
            line += ".";
        }

        return line;
    }

    public static bool IsValidPlainEnglishSummary(string? completion, string subject)
    {
        if (string.IsNullOrWhiteSpace(completion)) return false;

        var text = completion.Trim();
        if (text.Length < 18 || text.Length > 220) return false;

        // Reject if it ends with a colon, semicolon, or dash (incomplete preamble / label)
        var unpunctuated = text.TrimEnd('.', ' ', '\t');
        if (unpunctuated.EndsWith(':') ||
            unpunctuated.EndsWith(';') ||
            unpunctuated.EndsWith('-') ||
            text.EndsWith("..."))
        {
            return false;
        }

        // Reject if it contains URLs, web schemes, or domain markers
        if (text.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".com", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".org", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".net", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".png", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".gif", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".html", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".aspx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Reject if it contains unparsed markup or bracket artifacts
        if (text.Contains('<') || text.Contains('>') || text.Contains('[') || text.Contains(']') ||
            text.Contains('{') || text.Contains('}') || text.Contains('\\'))
        {
            return false;
        }

        // Reject prompt scaffolding, meta preambles, or degenerate openers
        if (text.StartsWith("Email:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("From:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Body:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Write a", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Task:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Summarize", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Summary", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("The following", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("The above", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Here is", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("In summary", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check word count
        var words = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 5) return false;

        // Check letter ratio (must be predominantly alphabet letters, not symbols)
        int letterCount = text.Count(char.IsLetter);
        if ((double)letterCount / text.Length < 0.65) return false;

        // Reject if meaningful words repeat in short completion (e.g. "following ... following")
        var nonStopWords = words
            .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '"', '\'').ToLowerInvariant())
            .Where(w => w.Length > 3 && w != "that" && w != "with" && w != "from" && w != "this" && w != "have")
            .ToList();

        if (nonStopWords.GroupBy(w => w).Any(g => g.Count() >= 2) && words.Length <= 10)
        {
            return false;
        }

        // Check for repeating 3-word ngrams (hallucination loops)
        for (int i = 0; i <= words.Length - 6; i++)
        {
            var gram = $"{words[i]} {words[i + 1]} {words[i + 2]}".ToLowerInvariant();
            var remainder = string.Join(" ", words.Skip(i + 3)).ToLowerInvariant();
            if (remainder.Contains(gram))
            {
                return false;
            }
        }

        return true;
    }

    private record MlGenerateRequest(
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("maxTokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] float Temperature
    );

    private record MlGenerateResponse(
        [property: JsonPropertyName("completion")] string? Completion,
        [property: JsonPropertyName("latencyMs")] double LatencyMs
    );
}
