namespace MailStripper.Security;

public static class SecurityConstants
{
    public const string ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self' http://localhost:8000; font-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self';";
    public const long MaxUploadSizeBytes = 50 * 1024 * 1024; // 50 MB
}
