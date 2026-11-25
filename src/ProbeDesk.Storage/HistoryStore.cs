using Microsoft.EntityFrameworkCore;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Storage;

/// <summary>Metadata-only history. Never stores query strings, headers, cookies or bodies.</summary>
public sealed class HistoryStore(IDbContextFactory<ProbeDeskDbContext> factory)
{
    public const int MaxEntries = 500;

    /// <summary>Replaces path segments that look like ids or tokens (digits, GUIDs, long opaque strings) with ":id".</summary>
    internal static string Redact(string path) =>
        string.Join('/', path.Split('/').Select(s =>
            s.Length > 0 && (s.All(char.IsDigit) || Guid.TryParse(s, out _)
                || (s.Length >= 20 && s.Any(char.IsDigit) && s.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '='))) ? ":id" : s));

    public async Task AddAsync(string method, string url, int status, long durationMs)
    {
        var host = ""; var path = "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) { host = u.Authority; path = Redact(u.AbsolutePath); }

        await using var db = await factory.CreateDbContextAsync();
        db.History.Add(new HistoryEntity
        {
            Id = Guid.NewGuid(), TimeUtc = DateTime.UtcNow, Method = method.ToUpperInvariant(),
            Host = host, Path = path, Status = status, DurationMs = durationMs
        });
        await db.SaveChangesAsync();

        var excess = await db.History.CountAsync() - MaxEntries;
        if (excess > 0)
        {
            var oldest = await db.History.OrderBy(h => h.TimeUtc).Take(excess).Select(h => h.Id).ToListAsync();
            await db.History.Where(h => oldest.Contains(h.Id)).ExecuteDeleteAsync();
        }
    }

    public async Task<List<HistoryEntry>> ListAsync(int take = 200)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.History.AsNoTracking().OrderByDescending(h => h.TimeUtc).Take(take).ToListAsync())
            .Select(h => new HistoryEntry(h.Id, h.TimeUtc, h.Method, h.Host, h.Path, h.Status, h.DurationMs)).ToList();
    }

    public async Task ClearAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.History.ExecuteDeleteAsync();
    }
}
