using System.Text;
using MailStripper.Services;
using Xunit;

namespace MailStripper.Tests;

public class EmailValidatorTests
{
    [Fact]
    public void Validate_RejectsMp4VideoByExtension()
    {
        using var stream = new MemoryStream(new byte[] { 0x00, 0x01, 0x02 });
        var result = EmailValidator.Validate(stream, "recording.mp4");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("video file", result.ErrorMessage);
        Assert.Contains("not an email message", result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsMp4VideoByMagicBytesEvenWithEmlExtension()
    {
        // MP4 magic: bytes 4..7 are 'ftyp'
        var mp4Header = new byte[] { 0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2' };
        using var stream = new MemoryStream(mp4Header);
        var result = EmailValidator.Validate(stream, "sneaky_video.eml");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("video (MP4/ISO Media)", result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsBinaryWithNullBytes()
    {
        var binary = new byte[] { 0x00, 0x00, 0xFF, 0xFE, 0x10, 0x20 };
        using var stream = new MemoryStream(binary);
        var result = EmailValidator.Validate(stream, "test.eml");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("null bytes detected", result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsZipArchive()
    {
        var zipMagic = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
        using var stream = new MemoryStream(zipMagic);
        var result = EmailValidator.Validate(stream, "archive.eml");

        Assert.False(result.IsValid);
        Assert.Contains("ZIP archive", result.ErrorMessage);
    }

    [Fact]
    public void Validate_AllowsValidEmailStream()
    {
        var sampleBytes = SampleEmailService.GenerateSampleEml();
        using var stream = new MemoryStream(sampleBytes);
        var result = EmailValidator.Validate(stream, "valid_email.eml");

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidatePastedText_RejectsNullBytesAndEmpty()
    {
        var emptyResult = EmailValidator.ValidatePastedText("   ");
        Assert.False(emptyResult.IsValid);

        var nullResult = EmailValidator.ValidatePastedText("Hello\0World");
        Assert.False(nullResult.IsValid);
        Assert.Contains("null binary bytes", nullResult.ErrorMessage);

        var validResult = EmailValidator.ValidatePastedText("From: Alice <alice@example.com>\nSubject: Hi\n\nHello");
        Assert.True(validResult.IsValid);
    }
}
