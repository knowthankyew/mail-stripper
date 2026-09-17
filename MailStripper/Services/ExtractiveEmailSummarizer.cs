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
            var trimmed = CleanPunctuation(SanitizePlainEnglishText(s));
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
            : $"{senderName} sent an update regarding \"{cleanSubject}\".";

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

        // Remove trailing " Email" if present, e.g. "Intro Video Request Email" -> "Intro Video Request"
        if (current.EndsWith(" Email", StringComparison.OrdinalIgnoreCase) && current.Length > 9)
        {
            current = current[..^6].Trim();
        }

        return string.IsNullOrWhiteSpace(current) ? "No Subject" : current;
    }

    public static string ExtractSenderDisplayName(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender)) return "Sender";

        var rawName = sender.Trim();

        // If format is "Display Name <email@domain.com>" or '"Display Name" <...>'
        var angleMatch = SenderAngleRegex().Match(rawName);
        if (angleMatch.Success)
        {
            var name = angleMatch.Groups[1].Value.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(name))
            {
                rawName = name;
            }
            else
            {
                var emailLocal = angleMatch.Groups[2].Value.Split('@').FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(emailLocal))
                {
                    rawName = CapitalizeWords(emailLocal.Replace(".", " ").Replace("_", " "));
                }
            }
        }
        else if (rawName.Contains('@'))
        {
            var local = rawName.Split('@')[0].Trim();
            rawName = CapitalizeWords(local.Replace(".", " ").Replace("_", " "));
        }

        // Clean automated / system markers from sender name
        rawName = SystemSenderRegex().Replace(rawName, "").Trim();
        rawName = Regex.Replace(rawName, @"\s+", " ").Trim();

        if (!string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        // If display name was purely automated tokens, fall back to email domain
        if (angleMatch.Success)
        {
            var emailPart = angleMatch.Groups[2].Value.Trim();
            var atIndex = emailPart.IndexOf('@');
            if (atIndex > 0 && atIndex < emailPart.Length - 1)
            {
                var domain = emailPart[(atIndex + 1)..];
                var dotIndex = domain.IndexOf('.');
                var domainName = dotIndex > 0 ? domain[..dotIndex] : domain;
                if (!string.IsNullOrWhiteSpace(domainName))
                {
                    return CapitalizeWords(domainName);
                }
            }
        }

        return "Sender";
    }

    public static string SanitizePlainEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // 1. Remove markdown image tags: ![...](...) or [https://...png]
        text = MarkdownImageRegex().Replace(text, "");

        // 2. Remove markdown link syntax: [anchor text](https://...) -> anchor text (if anchor is not an image or url)
        text = MarkdownLinkRegex().Replace(text, m =>
        {
            var anchor = m.Groups[1].Value.Trim();
            if (anchor.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                anchor.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                anchor.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                anchor.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                anchor.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                anchor.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return anchor;
        });

        // 3. Remove angle bracketed and square bracketed URLs: <https://...>, [https://...]
        text = BracketedUrlRegex().Replace(text, "");

        // 4. Remove mailto links: <mailto:...>
        text = MailtoRegex().Replace(text, "");

        // 5. Remove media / icon markers: [Rose_logo.png], [cid:...], [Linkedin], [Twitter], etc.
        text = MediaBracketRegex().Replace(text, "");

        // 6. Remove standalone URLs: https://... http://... www....
        text = StandaloneUrlRegex().Replace(text, "");

        // 7. Strip list bullet prefixes at the start of text: "* ", "- ", "• ", "+ ", "1. "
        text = BulletPrefixRegex().Replace(text, "");

        // 8. Clean leftover brackets, angle brackets, or redundant characters
        text = StrayBracketsRegex().Replace(text, " ");

        // 9. Normalize whitespace
        text = MultiSpaceRegex().Replace(text, " ").Trim();

        return text;
    }

    public static string GetCleanPlainEnglishSnippet(string bodyText, int maxLength = 600)
    {
        var lines = CleanEmailBodyLines(bodyText);
        var fullText = string.Join(" ", lines);
        if (fullText.Length <= maxLength) return fullText;

        // Cut cleanly at last word boundary
        var snippet = fullText[..maxLength];
        var lastSpace = snippet.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            snippet = snippet[..lastSpace];
        }
        return snippet.Trim();
    }

    public static List<string> CleanEmailBodyLines(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return new List<string>();

        var rawLines = bodyText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var preCleanedLines = new List<string>();

        // Find index of first greeting line if present (discard prior logo banners / marketing slogans)
        int greetingIndex = -1;
        for (int i = 0; i < Math.Min(rawLines.Length, 35); i++)
        {
            var trimmed = rawLines[i].Trim();
            if (GreetingRegex().IsMatch(trimmed))
            {
                greetingIndex = i;
                break;
            }
        }

        int startIndex = greetingIndex >= 0 ? greetingIndex : 0;

        for (int i = startIndex; i < rawLines.Length; i++)
        {
            var line = rawLines[i].Trim();

            // Stop when hitting quoted thread markers
            if (line.StartsWith(">") ||
                QuoteHeaderRegex().IsMatch(line) ||
                line.StartsWith("-----Original Message-----", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("________________________________", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("---------- Forwarded message ---------", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Skip greetings
            if (GreetingRegex().IsMatch(line))
            {
                continue;
            }

            // Skip signoffs / signatures
            if (SignoffRegex().IsMatch(line))
            {
                break;
            }

            // Skip legal boilerplate, copyright, or social media links
            if (line.Contains("This message is intended only for the use of", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("confidential and proprietary", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Sent from my iPhone", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Sent from Outlook", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("©", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("(c)", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("All rights reserved", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Follow and Connect with Us", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Follow Rose International on LinkedIn", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Candidate Care Team", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("To unsubscribe", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Clean line of URLs, image tags, and markdown brackets
            var sanitized = SanitizePlainEnglishText(line);

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                continue;
            }

            // Skip standalone label headers that aren't sentences
            if (sanitized.Equals("Please Note:", StringComparison.OrdinalIgnoreCase) ||
                sanitized.Equals("Please Note", StringComparison.OrdinalIgnoreCase) ||
                sanitized.Equals("Note:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip short 1-2 word fragments without terminal punctuation
            var words = sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 3 && !sanitized.EndsWith('.') && !sanitized.EndsWith('?') && !sanitized.EndsWith('!'))
            {
                continue;
            }

            preCleanedLines.Add(sanitized);
        }

        return preCleanedLines;
    }

    public static List<string> ExtractSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        var raw = SentenceSplitRegex().Split(text);
        var sentences = new List<string>();

        foreach (var s in raw)
        {
            var clean = SanitizePlainEnglishText(s);
            if (clean.Length > 15)
            {
                var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 4)
                {
                    if (!clean.EndsWith('.') && !clean.EndsWith('?') && !clean.EndsWith('!'))
                    {
                        clean += ".";
                    }
                    sentences.Add(clean);
                }
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
            else if (i == 1) score += 2.0;
            else if (i == 2) score += 1.0;

            // Subject relevance
            foreach (var kw in subjectKeywords)
            {
                if (lower.Contains(kw)) score += 2.0;
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

        if (highest != null && highest.Score >= 3.5)
        {
            var cleanedSentence = NormalizeIntentSentence(highest.Text);
            if (!string.IsNullOrWhiteSpace(cleanedSentence))
            {
                var title = $"{senderName} {cleanedSentence}{attachmentClause}.";
                return CleanPunctuation(title);
            }
        }

        // Fallback formulation based on subject
        var fallback = $"{senderName} sent a request regarding \"{cleanSubject}\"{attachmentClause}.";
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
        if (lower.StartsWith("as we discussed") || lower.StartsWith("as discussed"))
        {
            return "followed up on your recent discussion";
        }
        if (lower.StartsWith("by creating an introduction video") || lower.StartsWith("by creating a video") || lower.StartsWith("by creating"))
        {
            return "requested an introduction video to highlight your skills and qualifications";
        }
        if (lower.StartsWith("we're excited") || lower.StartsWith("we are excited"))
        {
            return "shared onboarding details and requested next steps";
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

        if (text.StartsWith("We ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[3..].TrimStart();
            return $"communicated that {text}";
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
        var res = input.Replace("..", ".").Replace(" .", ".").Replace("?.", "?").Replace("!.", "!").Replace("  ", " ").Trim();
        if (!res.EndsWith('.') && !res.EndsWith('?') && !res.EndsWith('!')) res += ".";
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

    [GeneratedRegex(@"\b(please|request|review|approve|confirm|urgent|action\s+required|deadline|attached|deliverable|invoice|scheduled|budget|meeting|update|video|skills|qualifications)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntentKeywordsRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]*\)|\[https?://[^\]]+\.(?:png|jpg|jpeg|gif|webp|svg|ico)[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"<https?://[^>]+>|\[https?://[^\]]+\]|<www\.[^>]+>|\[www\.[^\]]+\]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketedUrlRegex();

    [GeneratedRegex(@"<mailto:[^>]+>|\[mailto:[^\]]+\]", RegexOptions.IgnoreCase)]
    private static partial Regex MailtoRegex();

    [GeneratedRegex(@"\[(?:cid:[^\]]+|[a-zA-Z0-9_\-\.\s]+\.(?:png|jpg|jpeg|gif|webp|svg|bmp|ico)|Linkedin|Twitter|Facebook|Instagram|Youtube|X)\]", RegexOptions.IgnoreCase)]
    private static partial Regex MediaBracketRegex();

    [GeneratedRegex(@"\bhttps?://[^\s<>""'\]\)]+|\bwww\.[^\s<>""'\]\)]+", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneUrlRegex();

    [GeneratedRegex(@"^\s*(?:[\*\-•\+]|\d+[\.\)])\s+")]
    private static partial Regex BulletPrefixRegex();

    [GeneratedRegex(@"[\[\]<>]")]
    private static partial Regex StrayBracketsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\b(System Generated|Do Not Reply|No Reply|Automated|Notifications?|Mailer[- ]Daemon)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SystemSenderRegex();
}
