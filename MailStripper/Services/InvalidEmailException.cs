namespace MailStripper.Services;

public class InvalidEmailException : Exception
{
    public string? DetectedType { get; }

    public InvalidEmailException(string message, string? detectedType = null)
        : base(message)
    {
        DetectedType = detectedType;
    }
}
