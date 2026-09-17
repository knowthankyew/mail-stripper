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
}
