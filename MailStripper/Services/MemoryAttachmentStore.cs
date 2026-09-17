using System.Collections.Concurrent;
using System.IO.Compression;
using MailStripper.Models;

namespace MailStripper.Services;

public class MemoryAttachmentStore : IAttachmentStore, IDisposable
{
    private record SessionContainer(DateTime CreatedAt, List<AttachmentData> Items);

    private readonly ConcurrentDictionary<string, SessionContainer> _cache = new();
    private readonly TimeSpan _retention = TimeSpan.FromMinutes(45);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _cleanupLoop;

    public MemoryAttachmentStore()
    {
        _cleanupLoop = Task.Run(PeriodicCleanupLoopAsync);
    }

    private async Task PeriodicCleanupLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(_cts.Token);
                CleanupExpired();
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Store(string sessionId, IEnumerable<AttachmentData> attachments)
    {
        CleanupExpired();
        var list = attachments.ToList();
        _cache[sessionId] = new SessionContainer(DateTime.UtcNow, list);
    }

    public AttachmentData? GetAttachment(string sessionId, int index)
    {
        CleanupExpired();
        if (_cache.TryGetValue(sessionId, out var container))
        {
            if (index >= 0 && index < container.Items.Count)
            {
                return container.Items[index];
            }
        }
        return null;
    }

    public AttachmentData? GetAttachmentById(string sessionId, string attachmentId)
    {
        if (_cache.TryGetValue(sessionId, out var container))
        {
            return container.Items.FirstOrDefault(a => a.Id == attachmentId);
        }
        return null;
    }

    public bool HasAttachments(string sessionId)
    {
        return _cache.TryGetValue(sessionId, out var container) && container.Items.Count > 0;
    }

    public byte[]? CreateZipArchive(string sessionId, string emailSubject)
    {
        if (!_cache.TryGetValue(sessionId, out var container) || container.Items.Count == 0)
        {
            return null;
        }

        using var memoryStream = new MemoryStream();
        using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in container.Items)
            {
                var safeName = SanitizeFileName(item.FileName);
                if (string.IsNullOrWhiteSpace(safeName))
                {
                    safeName = $"attachment_{item.Index + 1}.bin";
                }

                // Ensure unique file names within zip
                var finalName = safeName;
                var counter = 1;
                var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
                var ext = Path.GetExtension(safeName);

                while (!usedNames.Add(finalName))
                {
                    finalName = $"{nameWithoutExt}_{counter++}{ext}";
                }

                var entry = zip.CreateEntry(finalName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(item.Data, 0, item.Data.Length);
            }
        }

        return memoryStream.ToArray();
    }

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _retention;
        foreach (var key in _cache.Keys)
        {
            if (_cache.TryGetValue(key, out var session) && session.CreatedAt < cutoff)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var clean = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "attachment" : clean;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
