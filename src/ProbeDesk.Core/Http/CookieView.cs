using System.Net;

namespace ProbeDesk.Core.Http;

public sealed record CookieInfo(string Domain, string Path, string Name, string Value, DateTime? Expires, bool Secure, bool HttpOnly);

/// <summary>List, edit and delete cookies in a session's cookie jar.</summary>
public static class CookieView
{
    public static List<CookieInfo> List(CookieContainer jar) =>
        jar.GetAllCookies().Where(c => !c.Expired)
            .Select(c => new CookieInfo(c.Domain.TrimStart('.'), c.Path, c.Name, c.Value,
                c.Expires == DateTime.MinValue ? null : c.Expires, c.Secure, c.HttpOnly))
            .OrderBy(c => c.Domain).ThenBy(c => c.Name).ToList();

    public static void Set(CookieContainer jar, string domain, string path, string name, string value) =>
        jar.Add(new Cookie(name, value, string.IsNullOrEmpty(path) ? "/" : path, domain.TrimStart('.')));

    public static void Delete(CookieContainer jar, string domain, string path, string name)
    {
        // Expiring a cookie removes it from the container.
        var c = new Cookie(name, "", path, domain) { Expires = DateTime.UtcNow.AddDays(-1) };
        jar.Add(c);
        foreach (var existing in jar.GetAllCookies().Where(x =>
                     x.Name == name && x.Domain.TrimStart('.') == domain.TrimStart('.') && x.Path == path))
            existing.Expired = true;
    }

    public static void ClearDomain(CookieContainer jar, string domain)
    {
        foreach (var c in jar.GetAllCookies().Where(x => x.Domain.TrimStart('.') == domain.TrimStart('.')))
            c.Expired = true;
    }

    public static void ClearAll(CookieContainer jar)
    {
        foreach (var c in jar.GetAllCookies().Cast<Cookie>()) c.Expired = true;
    }
}
