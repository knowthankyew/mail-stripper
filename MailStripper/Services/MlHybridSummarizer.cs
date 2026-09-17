using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MailStripper.Services;

public class SummarizerOptions
{
    public string MlEndpoint { get; set; } = "http://localhost:8000/api/v1/inference/generate";
    public string HealthEndpoint { get; set; } = "http://localhost:8000/healthz";
    public bool EnableMl { get; set; } = true;
    public int TimeoutMs { get; set; } = 3000;
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
            var snippet = bodyText.Length > 600 ? bodyText[..600] : bodyText;
            var attClause = attachmentNames.Count > 0 ? string.Join(", ", attachmentNames) : "none";

            var prompt = $"Summarize this email in ONE crisp sentence stating what {senderName} needs or communicated:\n" +
                         $"Subject: {cleanSubject}\n" +
                         $"Body: {snippet}\n" +
                         $"Attachments: {attClause}\n" +
                         $"Summary:";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(_options.Value.TimeoutMs));

            var payload = new MlGenerateRequest(
                Prompt: prompt,
                MaxTokens: 60,
                Temperature: 0.2f
            );

            var response = await _httpClient.PostAsJsonAsync(_options.Value.MlEndpoint, payload, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var mlData = await response.Content.ReadFromJsonAsync<MlGenerateResponse>(cancellationToken: cts.Token);
                if (mlData != null && !string.IsNullOrWhiteSpace(mlData.Completion))
                {
                    var cleanCompletion = CleanMlCompletion(mlData.Completion);
                    if (cleanCompletion.Length > 15)
                    {
                        return extractiveResult with
                        {
                            OneSentenceTitle = cleanCompletion,
                            EngineName = "SmolLM2-135M (FTaaS ML + MimeKit)"
                        };
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

    private static string CleanMlCompletion(string raw)
    {
        var line = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        line = line.Trim().Trim('"', '\'');
        if (line.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
        {
            line = line[8..].Trim();
        }

        if (!line.EndsWith('.'))
        {
            line += ".";
        }

        return line;
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
