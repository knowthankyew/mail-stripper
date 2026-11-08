namespace MailStripper.Services;

/// <summary>
/// Configuration options for air-gapped unsubscribe forensic analysis and RFC 8601 authserv-id verification.
/// </summary>
public class UnsubscribeForensicsOptions
{
    /// <summary>
    /// Optional expected receiving MTA authserv-id (e.g. "mx.google.com" or "mail.corp.internal").
    /// When specified, Authentication-Results headers are only elevated to TrustedReceivingMx if
    /// the authserv-id matches and the topmost non-internal Received hop corroborates the receiving
    /// MTA boundary (exact host or subdomain match).
    /// Default is null (all static .eml uploads remain strictly labeled UnverifiedUpload).
    /// </summary>
    public string? TrustedAuthservId { get; set; }
}
