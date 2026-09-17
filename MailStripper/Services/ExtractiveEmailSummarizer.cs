using System.Text;
using System.Text.RegularExpressions;

namespace MailStripper.Services;

public partial class ExtractiveEmailSummarizer : IEmailSummarizer
{
    public Task<SummaryResult> SummarizeAsync(
        string subject,
        string sender,
        string bodyText,
        IReadOnlyList<string> attachmentNames,
        CancellationToken ct = default)
    {
        var cleanSubject = CleanSubject(subject);
        var senderName = ExtractSenderDisplayName(sender);
        var cleanedLines = CleanEmailBodyLines(bodyText);
        var cleanedText = string.Join(" ", cleanedLines);

        var sentences = ExtractSentences(cleanedText);
        var scoredSentences = ScoreSentences(sentences, cleanSubject, attachmentNames);

        // Formulate One-Sentence Title
        var oneSentenceTitle = BuildOneSentenceTitle(senderName, cleanSubject, scoredSentences, attachmentNames);

        // Build Short Summary (2-4 key points or clean paragraph)
        var topSentences = scoredSentences
            .OrderByDescending(s => s.Score)
            .Take(3)
            .OrderBy(s => s.OriginalIndex)
            .Select(s => s.Text)
            .ToList();

        var bulletPoints = new List<string>();
        foreach (var s in topSentences)
        {
            var trimmed = s.Trim();
            if (trimmed.Length > 10 && !bulletPoints.Contains(trimmed))
            {
                bulletPoints.Add(trimmed);
            }
        }

        if (attachmentNames.Count > 0)
        {
            var namesPreview = string.Join(", ", attachmentNames.Take(3));
            if (attachmentNames.Count > 3)
            {
                namesPreview += $" and {attachmentNames.Count - 3} more";
            }
            bulletPoints.Add($"Includes {attachmentNames.Count} attachment(s): {namesPreview}.");
        }

        var shortSummary = bulletPoints.Count > 0
            ? string.Join(" ", bulletPoints)
            : $"Email from {senderName} regarding \"{cleanSubject}\".";

        return Task.FromResult(new SummaryResult(
            OneSentenceTitle: oneSentenceTitle,
            ShortSummary: shortSummary,
            KeyBulletPoints: bulletPoints,
            EngineName: "Smart Extractive (jstedfast/MimeKit)"
        ));
    }

    public static string CleanSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "No Subject";

        var current = subject.Trim();
        bool changed;
        do
        {
            changed = false;
            var match = SubjectPrefixRegex().Match(current);
            if (match.Success)
            {
                current = current[match.Length..].Trim();
                changed = true;
            }
        } while (changed);

        return string.IsNullOrWhiteSpace(current) ? "No Subject" : current;
    }

    public static string ExtractSenderDisplayName(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender)) return "Sender";

        // If format is "Display Name <email@domain.com>" or '"Display Name" <...>'
        var angleMatch = SenderAngleRegex().Match(sender);
        if (angleMatch.Success)
        {
            var name = angleMatch.Groups[1].Value.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
            var emailLocal = angleMatch.Groups[2].Value.Split('@').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(emailLocal))
            {
                return CapitalizeWords(emailLocal.Replace(".", " ").Replace("_", " "));
            }
        }

        // Just email
        if (sender.Contains('@'))
        {
            var local = sender.Split('@')[0].Trim();
            return CapitalizeWords(local.Replace(".", " ").Replace("_", " "));
        }

        return sender.Trim();
    }

    private static List<string> CleanEmailBodyLines(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return new List<string>();

        var rawLines = bodyText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new List<string>();

        foreach (var rawLine in rawLines)
        {
            var line = rawLine.Trim();

            // Stop when hitting quoted thread markers
            if (line.StartsWith(">") ||
                QuoteHeaderRegex().IsMatch(line) ||
                line.StartsWith("-----Original Message-----", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("________________________________", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Skip greetings
            if (GreetingRegex().IsMatch(line) && result.Count <= 2)
            {
                continue;
            }

            // Skip signoffs / signatures
            if (SignoffRegex().IsMatch(line))
            {
                break;
            }

            // Skip legal boilerplate
            if (line.Contains("This message is intended only for the use of", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("confidential and proprietary", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Sent from my iPhone", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Sent from Outlook", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static List<string> ExtractSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        var raw = SentenceSplitRegex().Split(text);
        var sentences = new List<string>();

        foreach (var s in raw)
        {
            var clean = s.Trim();
            if (clean.Length > 12)
            {
                sentences.Add(clean);
            }
        }

        return sentences;
    }

    private record ScoredSentence(string Text, double Score, int OriginalIndex);

    private static List<ScoredSentence> ScoreSentences(
        List<string> sentences,
        string subject,
        IReadOnlyList<string> attachmentNames)
    {
        var subjectKeywords = subject.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        var scored = new List<ScoredSentence>();

        for (int i = 0; i < sentences.Count; i++)
        {
            var s = sentences[i];
            var lower = s.ToLowerInvariant();
            double score = 1.0;

            // Position bonus (earlier sentences in clean email body typically hold intent)
            if (i == 0) score += 3.0;
            else if (i == 1) score += 1.8;
            else if (i == 2) score += 1.0;

            // Subject relevance
            foreach (var kw in subjectKeywords)
            {
                if (lower.Contains(kw)) score += 1.5;
            }

            // Intent verbs & calls to action
            if (IntentKeywordsRegex().IsMatch(lower))
            {
                score += 3.5;
            }

            // Attachment mentions
            if (lower.Contains("attach") || lower.Contains("file") || lower.Contains("pdf") || lower.Contains("document"))
            {
                score += 2.5;
            }

            // Question / request marker
            if (s.EndsWith('?'))
            {
                score += 1.2;
            }

            // Penalty for very short or overly long fragments
            if (s.Length < 25) score *= 0.6;
            if (s.Length > 240) score *= 0.8;

            scored.Add(new ScoredSentence(s, score, i));
        }

        return scored;
    }

    private static string BuildOneSentenceTitle(
        string senderName,
        string cleanSubject,
        List<ScoredSentence> scoredSentences,
        IReadOnlyList<string> attachmentNames)
    {
        var highest = scoredSentences.OrderByDescending(s => s.Score).FirstOrDefault();

        var attachmentClause = string.Empty;
        if (attachmentNames.Count == 1)
        {
            attachmentClause = $" and attached {attachmentNames[0]}";
        }
        else if (attachmentNames.Count > 1)
        {
            attachmentClause = $" and attached {attachmentNames.Count} files";
        }

        if (highest != null && highest.Score >= 4.0)
        {
            var cleanedSentence = NormalizeIntentSentence(highest.Text);
            if (!string.IsNullOrWhiteSpace(cleanedSentence))
            {
                var title = $"{senderName} {cleanedSentence}{attachmentClause}.";
                return CleanPunctuation(title);
            }
        }

        // Fallback formulation based on subject
        var fallback = $"{senderName} sent an update regarding \"{cleanSubject}\"{attachmentClause}.";
        return CleanPunctuation(fallback);
    }

    private static string NormalizeIntentSentence(string sentence)
    {
        var text = sentence.Trim().TrimEnd('.', ';', '!', ',');
        var lower = text.ToLowerInvariant();

        if (lower.StartsWith("please find attached") || lower.StartsWith("here is") || lower.StartsWith("attached is"))
        {
            return "shared the requested documents";
        }
        if (lower.StartsWith("could you please review") || lower.StartsWith("please review"))
        {
            return "is requesting a review of the provided details";
        }
        if (lower.StartsWith("i am writing to confirm") || lower.StartsWith("confirming that"))
        {
            return "confirmed the scheduled items";
        }
        if (lower.StartsWith("i am writing to request") || lower.StartsWith("we would like to request"))
        {
            return "is requesting approval and feedback";
        }
        if (lower.StartsWith("i wanted to follow up") || lower.StartsWith("following up on"))
        {
            return "followed up on the current status";
        }

        // If it starts with "I " or "We "
        if (text.StartsWith("I ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..].TrimStart();
            return $"notes that they {text}";
        }

        // Keep it concise
        if (text.Length > 90)
        {
            text = text[..87] + "...";
        }

        return $"notes: \"{text}\"";
    }

    private static string CleanPunctuation(string input)
    {
        var res = input.Replace("..", ".").Replace(" .", ".").Replace("  ", " ");
        if (!res.EndsWith('.')) res += ".";
        return res;
    }

    private static string CapitalizeWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..].ToLowerInvariant() : "")));
    }

    [GeneratedRegex(@"^(?:(?:re|fwd|fw|aw|sv|urgent|external|ext)[\s:_-]+|\[[^\]]*\][\s:_-]*|\([^\)]*\)[\s:_-]*)+", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectPrefixRegex();

    [GeneratedRegex(@"^([^<]+)<([^>]+)>")]
    private static partial Regex SenderAngleRegex();

    [GeneratedRegex(@"^(?:on\s+.*wrote:|from:\s+.*|sent:\s+.*|to:\s+.*)", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteHeaderRegex();

    [GeneratedRegex(@"^(?:hi|hello|dear|hey|good\s+morning|good\s+afternoon|good\s+evening)\b.*", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingRegex();

    [GeneratedRegex(@"^(?:best\s+regards|regards|sincerely|thanks|thank\s+you|cheers|warmly|yours\s+truly)[\s,]*$", RegexOptions.IgnoreCase)]
    private static partial Regex SignoffRegex();

    [GeneratedRegex(@"(?<=[.?!])\s+(?=[A-Z0-9""'])")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\b(please|request|review|approve|confirm|urgent|action\s+required|deadline|attached|deliverable|invoice|scheduled|budget|meeting|update)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntentKeywordsRegex();
}
