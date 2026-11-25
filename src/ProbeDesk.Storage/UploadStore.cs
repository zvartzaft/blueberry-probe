namespace ProbeDesk.Storage;

public sealed class UploadTooLargeException(long maxBytes)
    : InvalidOperationException($"The file is larger than the limit of {maxBytes} bytes.");

/// <summary>Stores uploaded files in the app-data folder. Saved requests refer to them by path.</summary>
public sealed class UploadStore(string root, long maxBytes = 50L * 1024 * 1024)
{
    public long MaxBytes => maxBytes;

    public async Task<string> SaveAsync(Stream source, string fileName, CancellationToken ct = default)
    {
        var safe = string.Concat(Path.GetFileName(fileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (safe.Length == 0) safe = "file";
        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, safe);
        try
        {
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes) throw new UploadTooLargeException(maxBytes);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            return path;
        }
        catch
        {
            Directory.Delete(dir, true);
            throw;
        }
    }

    /// <summary>Deletes stored files that no saved request references and that are older than minAge.</summary>
    public int Cleanup(ISet<string> referencedPaths, TimeSpan minAge)
    {
        if (!Directory.Exists(root)) return 0;
        var removed = 0;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var files = Directory.GetFiles(dir);
            var inUse = files.Any(f => referencedPaths.Contains(f));
            if (inUse || DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) < minAge) continue;
            Directory.Delete(dir, true);
            removed++;
        }
        return removed;
    }
}
