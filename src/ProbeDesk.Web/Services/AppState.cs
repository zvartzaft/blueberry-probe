using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;
using ProbeDesk.Storage;

namespace ProbeDesk.Web.Services;

/// <summary>Current environment selection (single local user, so one shared instance).</summary>
public sealed class AppState(EnvironmentStore envs, SessionManager sessions)
{
    public RequestEnvironment? Environment { get; private set; }
    public event Action? Changed;

    public async Task SelectAsync(Guid? id)
    {
        var previous = Environment;
        Environment = id is null ? null : await envs.GetAsync(id.Value);
        // A changed base URL or TLS setting must not keep running on the old session.
        if (previous is not null && Environment is not null && previous.Id == Environment.Id
            && (previous.BaseUrl != Environment.BaseUrl || previous.IgnoreSsl != Environment.IgnoreSsl))
            sessions.Reset(Environment.Id);
        Changed?.Invoke();
    }

    public Task ReloadAsync() => SelectAsync(Environment?.Id);

    /// <summary>Tells the UI (for example the environment dropdown) that environments were added, renamed or deleted.</summary>
    public void NotifyEnvironmentsChanged() => Changed?.Invoke();

    /// <summary>One in-memory session per environment; a shared "no environment" session otherwise. Stored session cookies are loaded once per session.</summary>
    public HttpSession Session
    {
        get
        {
            var s = sessions.GetOrCreate(Environment?.Id ?? Guid.Empty, Environment?.IgnoreSsl ?? false);
            if (!s.CookiesSeeded)
            {
                s.CookiesSeeded = true;
                if (Environment is { SessionCookies.Count: > 0 } && TryGetBaseUri(out var uri))
                    SessionCookies.Seed(s, uri, Environment.SessionCookies);
            }
            return s;
        }
    }

    public void ResetSession() => sessions.Reset(Environment?.Id ?? Guid.Empty);

    public bool TryGetBaseUri(out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(Environment?.BaseUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && (uri = u) is not null;
    }

    /// <summary>Replaces the stored session cookies (empty clears them), saves the environment and starts a fresh session.</summary>
    public async Task SetSessionCookiesAsync(List<StoredCookie> cookies)
    {
        if (Environment is null) return;
        Environment.SessionCookies = cookies;
        await envs.SaveAsync(Environment);
        ResetSession();
        Changed?.Invoke();
    }

    /// <summary>Saves cookies the server refreshed during a run (for example a new antiforgery cookie). Only if cookies were stored before.</summary>
    public async Task PersistSessionCookiesAsync()
    {
        if (Environment is not { SessionCookies.Count: > 0 } || !TryGetBaseUri(out var uri)) return;
        var current = SessionCookies.Snapshot(Session, uri);
        if (current.Count == 0) return;
        var old = Environment.SessionCookies.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        if (current.SequenceEqual(old)) return;
        Environment.SessionCookies = current;
        try { await envs.SaveAsync(Environment); }
        catch (Exception) { /* best effort: the next run or a new paste stores them again */ }
    }
}
