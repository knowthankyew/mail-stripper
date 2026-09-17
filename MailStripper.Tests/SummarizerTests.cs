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
    [InlineData("Intro Video Request Email", "Intro Video Request")]
    public void CleanSubject_StripsPrefixes(string input, string expected)
    {
        var cleaned = ExtractiveEmailSummarizer.CleanSubject(input);
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("Alex Mercer <alex.mercer@example.com>", "Alex Mercer")]
    [InlineData("\"Vance, Devin\" <dvance@example.com>", "Vance, Devin")]
    [InlineData("elena.rostova@security.org", "Elena Rostova")]
    [InlineData("\"Rose International System Generated Do Not Reply\" <QCompassPortal@roseint.com>", "Rose International")]
    [InlineData("Automated Notifications <no-reply@company.com>", "Company")]
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

    [Fact]
    public async Task SummarizeAsync_StripsUrlsAndImageJunkFromMarketingEmail()
    {
        var summarizer = new ExtractiveEmailSummarizer();
        var subject = "Intro Video Request Email";
        var sender = "\"Rose International System Generated Do Not Reply\" <QCompassPortal@roseint.com>";
        var body = @"Rose International People Making IT Happen
[https://www.roseint.com/webdesign/RoseEmail/img/Rose_logo.png]<https://www.roseint.com>

People Making IT Happen

        [https://www.roseint.com/webdesign/RoseEmail/img/Contactus_small.jpg] <https://www.roseint.com/Contact.html>            [https://www.roseint.com/webdesign/RoseEmail/img/RoseJobs.jpg] <https://www.roseint.com/hotjobs/>               [https://www.roseint.com/webdesign/RoseEmail/img/Visit_Small.jpg] <https://www.roseint.com/>            [https://www.roseint.com/webdesign/RoseEmail/img/GettoKnow_small.jpg] <https://www.suebhatia.com/>

Hello Courtlandt,

We're excited that you have chosen to work with us and want to get to know you better! By creating an introduction video, you can highlight your skills/qualifications in your own words and put a face and personality behind your resume. This video is optional but may be shared with our client to help bring your story to life for them.

Click Here to Upload your Introduction Video<https://www.qcomx.com/UploadPortal/UploadVideo.aspx?i=Q18xMTE0MDAxMSNPXzUwNzQ5MiNTXzIjUV8wOTE2MjAyNjEzMjQzMQ==>

Please Note:

  *   The length of the video cannot exceed 60 seconds.
  *   The size of the video cannot exceed 100 MB.
  *   Only .mp4/.mov formats are accepted.

Rose International is proud to employ our country's veterans and we stay committed to hiring many more! If you are a military veteran, military reserve or military veteran/reserve spouse, we encourage you to highlight the valuable skills and experience you have gained through the military in your video.

Thanks,
Rose Qcompass Portal
Rose International is committed to providing outstanding service. If you would like to provide feedback...
Interested in the latest industry news? Follow Rose International on LinkedIn to stay connected.<https://www.linkedin.com/company/rose-international/>
Follow and Connect with Us Today!
[Linkedin]<https://www.linkedin.com/company/rose-international> [Twitter] <https://twitter.com/RoseInt>
© Rose International";

        var result = await summarizer.SummarizeAsync(subject, sender, body, new List<string>());

        Assert.NotNull(result);
        // The title should mention Rose International and be clean plain English
        Assert.Contains("Rose International", result.OneSentenceTitle);
        Assert.DoesNotContain("http", result.OneSentenceTitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".png", result.OneSentenceTitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", result.OneSentenceTitle);
        Assert.DoesNotContain(">", result.OneSentenceTitle);
        Assert.DoesNotContain("[", result.OneSentenceTitle);
        Assert.DoesNotContain("]", result.OneSentenceTitle);

        // Every bullet point must be clean plain English without URLs or brackets
        Assert.NotEmpty(result.KeyBulletPoints);
        foreach (var bullet in result.KeyBulletPoints)
        {
            Assert.DoesNotContain("http", bullet, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".png", bullet, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".jpg", bullet, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<", bullet);
            Assert.DoesNotContain(">", bullet);
            Assert.DoesNotContain("[", bullet);
            Assert.DoesNotContain("]", bullet);
        }
    }

    [Theory]
    [InlineData("[https://www.roseint.com/logo.png] <https://www.roseint.com>.", false)]
    [InlineData("The Q3 architecture is a new approach to. The Q3 architecture is a new approach to. The Q3 architecture", false)]
    [InlineData("Email: From: Cassian Brooks Subject: Architecture", false)]
    [InlineData("Short", false)]
    [InlineData("Cassian Brooks requested review of the revised Q3 distributed pipeline architecture.", true)]
    [InlineData("Rose International invited you to submit an optional introduction video.", true)]
    public void IsValidPlainEnglishSummary_ValidatesProperly(string input, bool expectedValid)
    {
        var valid = MlHybridSummarizer.IsValidPlainEnglishSummary(input, "Test Subject");
        Assert.Equal(expectedValid, valid);
    }
}
