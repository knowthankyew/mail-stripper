using System.Text;
using MimeKit;

namespace MailStripper.Services;

public class SampleEmailService
{
    public static byte[] GenerateSampleEml()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Cassian Brooks", "cassian.brooks@vanguard-tech.io"));
        message.To.Add(new MailboxAddress("Architecture Working Group", "arch-wg@vanguard-tech.io"));
        message.To.Add(new MailboxAddress("Devin Vance (CTO)", "dvance@vanguard-tech.io"));
        message.Subject = "Re: [URGENT] Q3 Distributed Pipeline Architecture & Budget Sign-Off";
        message.Date = DateTimeOffset.UtcNow.AddHours(-2);
        message.MessageId = $"<{Guid.NewGuid()}@vanguard-tech.io>";

        var builder = new BodyBuilder
        {
            TextBody = @"Hi Architecture Team,

Please review the attached updated Q3 distributed pipeline architecture and the revised infrastructure budget breakdown. 

We need final sign-off before 5:00 PM EST today so the cloud provisioning tickets can be approved for the staging cluster. 

Key updates in this revision:
1. Replaced legacy synchronous broker with high-throughput event streaming.
2. Estimated monthly AWS compute savings are projected at $14,200 (18.4% reduction).
3. The security compliance sign-off document has been finalized and verified by InfoSec.

Could you please review the attached files and reply with your approval?

Best regards,
Cassian Brooks
Principal Cloud Architect | Vanguard Tech
Sent from my MacBook Pro

________________________________
From: Devin Vance <dvance@vanguard-tech.io>
Sent: Wednesday, September 16, 2026 3:15 PM
To: Cassian Brooks <cassian.brooks@vanguard-tech.io>
Subject: Re: Q3 Distributed Pipeline Architecture

Cassian, send over the revised diagram and budget sheet as soon as InfoSec signs off.",

            HtmlBody = @"<div style='font-family: sans-serif; line-height: 1.6; color: #1e293b;'>
<p>Hi Architecture Team,</p>
<p>Please review the attached updated <strong>Q3 distributed pipeline architecture</strong> and the revised <strong>infrastructure budget breakdown</strong>.</p>
<p>We need final sign-off before <strong>5:00 PM EST today</strong> so the cloud provisioning tickets can be approved for the staging cluster.</p>
<p><strong>Key updates in this revision:</strong></p>
<ol>
  <li>Replaced legacy synchronous broker with high-throughput event streaming.</li>
  <li>Estimated monthly AWS compute savings are projected at <strong>$14,200</strong> (18.4% reduction).</li>
  <li>The security compliance sign-off document has been finalized and verified by InfoSec.</li>
</ol>
<p>Could you please review the attached files and reply with your approval?</p>
<p>Best regards,<br>
<strong>Cassian Brooks</strong><br>
Principal Cloud Architect | Vanguard Tech</p>
<hr style='border:none;border-top:1px solid #e2e8f0;margin:20px 0;'>
<div style='color:#64748b;font-size:12px;'>
<strong>From:</strong> Devin Vance &lt;dvance@vanguard-tech.io&gt;<br>
<strong>Sent:</strong> Wednesday, September 16, 2026 3:15 PM<br>
<strong>To:</strong> Cassian Brooks &lt;cassian.brooks@vanguard-tech.io&gt;<br>
<strong>Subject:</strong> Re: Q3 Distributed Pipeline Architecture
</div>
</div>"
        };

        // Attachment 1: CSV spreadsheet
        var csvContent = "Category,Current Monthly ($),Projected Q3 ($),Variance ($),Delta (%)\n" +
                         "Compute (EC2/EKS),48500,38200,-10300,-21.2%\n" +
                         "Storage (S3/EBS),12400,11100,-1300,-10.5%\n" +
                         "Networking & NAT,6800,4200,-2600,-38.2%\n" +
                         "Observability & Logs,9500,9500,0,0.0%\n" +
                         "Total,77200,63000,-14200,-18.4%\n";
        builder.Attachments.Add("q3_budget_breakdown.csv", Encoding.UTF8.GetBytes(csvContent), new ContentType("text", "csv"));

        // Attachment 2: Security compliance signoff text
        var signoffContent = "VANGUARD TECH - INFOSEC CLEARANCE RECORD\n" +
                             "=========================================\n" +
                             "Assessment ID: SEC-2026-09-8812\n" +
                             "System: Q3 Event-Driven Pipeline\n" +
                             "Lead Auditor: Elena Rostova (CISO Office)\n" +
                             "Status: APPROVED WITH CONDITIONS\n\n" +
                             "Conditions:\n" +
                             "1. All TLS 1.3 in-transit encryption certs rotated bi-monthly.\n" +
                             "2. PII masking filter enforced at ingestion gateway.\n\n" +
                             "Sign-off Date: 2026-09-17\n" +
                             "Cryptographic Fingerprint: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n";
        builder.Attachments.Add("security_compliance_signoff.txt", Encoding.UTF8.GetBytes(signoffContent), new ContentType("text", "plain"));

        // Attachment 3: A valid lightweight 1x1 PNG image as sample visual asset
        var samplePngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
            0x0B, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x01, 0xE5, 0x27, 0xDE, 0xFC, 0x00, 0x00, 0x00, 0x00, 0x49,
            0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
        };
        builder.Attachments.Add("pipeline_architecture_diagram.png", samplePngBytes, new ContentType("image", "png"));

        message.Body = builder.ToMessageBody();

        using var ms = new MemoryStream();
        message.WriteTo(ms);
        return ms.ToArray();
    }

    public static byte[] GenerateSampleNewsletterEml()
    {
        var raw =
            "Received: from mta-14.sendgrid.net (mta-14.sendgrid.net [149.72.18.14])\r\n" +
            "\tby mx.local.example with ESMTPS id q82f91;\r\n" +
            "\tSun, 08 Nov 2026 07:00:00 -0600\r\n" +
            "Authentication-Results: mx.local.example;\r\n" +
            "\tdkim=pass header.d=systems-digest.io header.s=s2026 header.b=ZmFrZVNp;\r\n" +
            "\tspf=pass smtp.mailfrom=bounces@systems-digest.io;\r\n" +
            "\tdmarc=pass (p=REJECT) header.from=systems-digest.io\r\n" +
            "DKIM-Signature: v=1; a=rsa-sha256; c=relaxed/relaxed; d=systems-digest.io; s=s2026;\r\n" +
            "\th=from:to:subject:date:list-unsubscribe:list-unsubscribe-post;\r\n" +
            "\tbh=47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=;\r\n" +
            "\tb=ZmFrZVNpZ25hdHVyZUJpdHNGb3JEZW1vRml4dHVyZUV2YWx1YXRpb24=\r\n" +
            "Return-Path: <bounces@systems-digest.io>\r\n" +
            "From: Systems Engineering Digest <digest@systems-digest.io>\r\n" +
            "To: Reader <engineer@local.example>\r\n" +
            "Subject: Issue #48: Zero-Copy Kernel I/O & Memory-Safe Parsers\r\n" +
            "Date: Sun, 08 Nov 2026 07:00:00 -0600\r\n" +
            "Message-ID: <issue-48-2026@systems-digest.io>\r\n" +
            "List-Unsubscribe: <mailto:leave-48@systems-digest.io?subject=unsubscribe>, <https://optout.systems-digest.io/rfc8058/one-click?sub=9f82a1>\r\n" +
            "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n\r\n" +
            "<div style='font-family:sans-serif;line-height:1.6;'>" +
            "<h2>Issue #48: Zero-Copy Kernel I/O</h2>" +
            "<p>This week we benchmarked io_uring buffer rings, non-backtracking DFA regular expressions, and volatile RAM stream processing across .NET 10 services.</p>" +
            "<ul><li>Latency p99 dropped by 34% under 50 MB payload saturation.</li><li>Zero temporary files touched disk during fuzzing runs.</li></ul>" +
            "<hr/><p style='font-size:12px;color:#64748b;'>You are subscribed to Systems Engineering Digest. " +
            "<a href='https://optout.systems-digest.io/preferences?sub=9f82a1'>Manage email preferences or unsubscribe</a>.</p>" +
            "</div>";

        return Encoding.UTF8.GetBytes(raw);
    }

    public static byte[] GenerateSamplePoisonTrapEml()
    {
        var raw =
            "Received: from unknown-relay.example ([198.51.100.77])\r\n" +
            "\tby mx.local.example with ESMTP id trap991;\r\n" +
            "\tSun, 08 Nov 2026 07:05:00 -0600\r\n" +
            "Authentication-Results: mx.local.example;\r\n" +
            "\tdkim=none;\r\n" +
            "\tspf=softfail smtp.mailfrom=alert@payroll-notify.example\r\n" +
            "From: Corporate Security Notice <alerts@vanguard-tech.io>\r\n" +
            "To: Employee <staff@vanguard-tech.io>\r\n" +
            "Subject: [Action Required] Mandatory MFA Token Re-Enrollment\r\n" +
            "Date: Sun, 08 Nov 2026 07:05:00 -0600\r\n" +
            "Message-ID: <mfa-trap-991@relay.invalid>\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n\r\n" +
            "<div style='font-family:sans-serif;line-height:1.6;'>" +
            "<p>Your corporate workstation token expires at 5:00 PM today. Review your session settings immediately to avoid lockout.</p>" +
            "<p>If you no longer wish to receive automated security alerts, click below:</p>" +
            "<p><a href='https://xn--vanguard-t3ch-99a.example/unsub-confirm?beacon=staff_active'>https://vanguard-tech.io/unsubscribe</a></p>" +
            "</div>";

        return Encoding.UTF8.GetBytes(raw);
    }
}
