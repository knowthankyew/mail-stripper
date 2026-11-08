using System.Text.Json.Serialization;

namespace MailStripper.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnsubscribeClassification
{
    NoUnsubscribeFound,
    OneClickPostCandidate,
    MailtoCandidate,
    HttpLandingCandidate,
    PoisonOrReconTrap
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthenticationProvenance
{
    UnverifiedUpload,
    MissingAuthHeaders,
    SyntheticPastedBody,
    AmbiguousOrForgedHeader,
    /// <summary>
    /// Message headers name the expected receiving MX (authserv-id match plus a corroborating Received hop).
    /// This is a structural, self-attested match on an uploaded file. It is NOT cryptographic authentication.
    /// </summary>
    TrustedReceivingMx
}

public class UnsubscribeCandidate
{
    public string Source { get; set; } = string.Empty;
    public string SanitizedUri { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string RegistrableDomain { get; set; } = string.Empty;
    public string AnchorText { get; set; } = string.Empty;
    public bool AlignedWithSenderOrEsp { get; set; }
    public List<string> RiskFlags { get; set; } = new();
}

public class UnsubscribeForensicReport
{
    public UnsubscribeClassification Classification { get; set; } = UnsubscribeClassification.NoUnsubscribeFound;
    public AuthenticationProvenance Provenance { get; set; } = AuthenticationProvenance.MissingAuthHeaders;
    public int RiskScore { get; set; }
    public string SummaryVerdict { get; set; } = "No unsubscribe headers or opt-out links detected in this message.";
    public string ReconWarning { get; set; } = string.Empty;

    public bool HeaderContextAvailable { get; set; } = true;
    public bool HasRfc8058OneClickPost { get; set; }
    public bool SingleUnambiguousHeader { get; set; } = true;
    public bool DkimCoversUnsubscribeHeaders { get; set; }
    public bool AuthResultsClaimsDkimPass { get; set; }
    public bool AuthResultsClaimsSpfPass { get; set; }
    public bool AuthResultsClaimsDmarcPass { get; set; }
    public bool AlignedWithSenderOrKnownEsp { get; set; }
    public string? AlignmentReason { get; set; }

    public string? MatchedAuthservId { get; set; }
    public string? ExpectedAuthservId { get; set; }
    /// <summary>
    /// True only when headers structurally match the expected authserv-id.
    /// Self-attested by the uploaded message; not proof of authenticity.
    /// </summary>
    public bool AuthservIdVerified { get; set; }

    public string? PrimaryTargetHost { get; set; }
    public string? SterileCurlCommand { get; set; }
    public string? SanitizedMailtoUri { get; set; }

    public List<string> PoisonSignals { get; set; } = new();
    public List<UnsubscribeCandidate> Candidates { get; set; } = new();
}
