namespace ProbeDesk.Core.Http;

/// <summary>One in-memory HttpSession per environment, expiring after an idle time-to-live.</summary>
public sealed class SessionManager(TimeSpan? ttl = null, TimeProvider? time = null) : IDisposable
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(60);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<Guid, Entry> _sessions = new();
    private readonly object _lock = new();

    private sealed class Entry(HttpSession session, bool ignoreSsl, DateTimeOffset used)
    {
        public HttpSession Session { get; } = session;
        public bool IgnoreSsl { get; } = ignoreSsl;
        public DateTimeOffset LastUsed { get; set; } = used;
    }

    public HttpSession GetOrCreate(Guid environmentId, bool ignoreSsl = false)
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            foreach (var k in _sessions.Where(e => now - e.Value.LastUsed > _ttl).Select(e => e.Key).ToList())
                Remove(k);

            if (_sessions.TryGetValue(environmentId, out var entry) && entry.IgnoreSsl != ignoreSsl)
            {
                Remove(environmentId);
                entry = null;
            }
            if (entry is null)
                _sessions[environmentId] = entry = new Entry(new HttpSession(ignoreSsl), ignoreSsl, now);
            entry.LastUsed = now;
            return entry.Session;
        }
    }

    public void Reset(Guid environmentId) { lock (_lock) Remove(environmentId); }

    private void Remove(Guid id)
    {
        if (_sessions.Remove(id, out var e)) e.Session.Dispose();
    }

    public void Dispose()
    {
        lock (_lock) foreach (var k in _sessions.Keys.ToList()) Remove(k);
    }
}
