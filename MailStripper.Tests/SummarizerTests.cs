using MailStripper.Services;
using Xunit;

namespace MailStripper.Tests;

public class SummarizerTests
{
    [Theory]
    [InlineData("Re: Fwd: [EXTERNAL] Urgent: Project Roadmap", "Project Roadmap")]
    [InlineData("FW: [SEC=OFFICIAL] Budget Review", "Budget Review")]
    [InlineData("Re: Re: Re: Status Update", "Status Update")]
    [InlineData("   No Prefix Here   ", "No Prefix Here")]
    public void CleanSubject_StripsPrefixes(string input, string expected)
    {
        var cleaned = ExtractiveEmailSummarizer.CleanSubject(input);
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("Alex Mercer <alex.mercer@example.com>", "Alex Mercer")]
    [InlineData("\"Vance, Devin\" <dvance@example.com>", "Vance, Devin")]
    [InlineData("elena.rostova@security.org", "Elena Rostova")]
    public void ExtractSenderDisplayName_ExtractsProperName(string input, string expected)
    {
        var name = ExtractiveEmailSummarizer.ExtractSenderDisplayName(input);
        Assert.Equal(expected, name);
    }

    [Fact]
    public async Task SummarizeAsync_GeneratesCrispOneSentenceTitleAndKeyBullets()
    {
        var summarizer = new ExtractiveEmailSummarizer();
        var subject = "Re: Q3 Contract Deliverables";
        var sender = "Cassian Brooks <cassian@vanguard.io>";
        var body = @"Hi Team,

Please review the attached updated Q3 distributed pipeline architecture and the revised budget breakdown. We need final sign-off before 5:00 PM EST today.

The security compliance sign-off document has been finalized.

Best regards,
Cassian Brooks";

        var attachments = new List<string> { "architecture.png", "budget.csv" };

        var result = await summarizer.SummarizeAsync(subject, sender, body, attachments);

        Assert.NotNull(result);
        Assert.NotEmpty(result.OneSentenceTitle);
        Assert.Contains("Cassian Brooks", result.OneSentenceTitle);
        Assert.EndsWith(".", result.OneSentenceTitle);
        Assert.NotEmpty(result.KeyBulletPoints);
        Assert.Contains("Smart Extractive", result.EngineName);
    }
}
