using MailStripper.Models;
using MimeKit;

namespace MailStripper.Services;

public interface IUnsubscribeForensicAnalyzer
{
    UnsubscribeForensicReport Analyze(MimeMessage message);
}
