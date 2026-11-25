using System.Net;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Core.Http;

/// <summary>
/// session cookie profile: parse a pasted browser <c>Cookie</c> header, keep only the auth and
/// antiforgery cookies, and move them between an environment and a session's cookie jar.
/// </summary>
public static class SessionCookies
{
    public const string AuthCookieName = ".AspNetCore.Cookies";
    public const string AntiforgeryPrefix = ".AspNetCore.Antiforgery.";
    public const string CsrfTokenPath = "/api/authentication/antiforgery/token";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string TestPath = "/api/users/me";

    public static bool IsProfileCookie(string name) =>
        name.Equals(AuthCookieName, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(AntiforgeryPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the profile cookies from a pasted header (with or without a leading "Cookie:"). Others are ignored.</summary>
    public static List<StoredCookie> Parse(string? raw)
    {
        var result = new Dictionary<string, StoredCookie>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var text = raw.Trim();
        if (text.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) text = text["Cookie:".Length..];

        foreach (var part in text.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (value.Length == 0 || !IsProfileCookie(name)) continue;
            result[name] = new StoredCookie(name, value);
        }
        return [.. result.Values];
    }

    public static bool HasAuthCookie(IEnumerable<StoredCookie> cookies) =>
        cookies.Any(c => c.Name.Equals(AuthCookieName, StringComparison.OrdinalIgnoreCase) && c.Value.Length > 0);

    /// <summary>Puts the stored cookies into the session jar for the base URL's host. Invalid cookies are skipped.</summary>
    public static void Seed(HttpSession session, Uri baseUri, IEnumerable<StoredCookie> cookies)
    {
        foreach (var c in cookies)
        {
            try { session.Cookies.Add(new Cookie(c.Name, c.Value, "/", baseUri.Host)); }
            catch (CookieException) { }
        }
    }

    /// <summary>Reads the current profile cookies for the base URL back from the jar (the server may have refreshed them).</summary>
    public static List<StoredCookie> Snapshot(HttpSession session, Uri baseUri) =>
        session.Cookies.GetCookies(baseUri).Cast<Cookie>()
            .Where(c => !c.Expired && IsProfileCookie(c.Name))
            .Select(c => new StoredCookie(c.Name, c.Value))
            .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
}
