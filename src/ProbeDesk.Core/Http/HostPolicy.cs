namespace ProbeDesk.Core.Http;

public sealed class BlockedHostException(string host)
    : InvalidOperationException($"Host '{host}' is not in the environment's allowed hosts.")
{
    public string Host { get; } = host;
}

public static class HostPolicy
{
    /// <summary>Empty list allows everything. Entries match exactly or as "*.suffix".</summary>
    public static bool IsAllowed(IReadOnlyList<string>? allowed, string host)
    {
        if (allowed is null || allowed.Count == 0) return true;
        foreach (var raw in allowed)
        {
            var a = raw.Trim();
            if (a.Length == 0) continue;
            if (a.StartsWith("*.", StringComparison.Ordinal))
            {
                if (host.EndsWith(a[1..], StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (host.Equals(a, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
