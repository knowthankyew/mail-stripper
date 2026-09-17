using MailStripper.Models;

namespace MailStripper.Services;

public interface IAttachmentStore
{
    void Store(string sessionId, IEnumerable<AttachmentData> attachments);
    AttachmentData? GetAttachment(string sessionId, int index);
    AttachmentData? GetAttachmentById(string sessionId, string attachmentId);
    byte[]? CreateZipArchive(string sessionId, string emailSubject);
    bool HasAttachments(string sessionId);
}
