using System.Text.Json;
using ProbeDesk.Core.Http;

namespace ProbeDesk.Core.Flow;

/// <summary>Fetches the CSRF token for the profile. The server may also re-issue the antiforgery cookie, which lands in the session jar.</summary>
public static class CsrfTokenProvider
{
    private static readonly HashSet<string> ReadOnly = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public static bool IsMutating(string method) => !ReadOnly.Contains(method);

    public static async Task<string> FetchAsync(HttpExecutor executor, Uri baseUri, CancellationToken ct = default)
    {
        var spec = new RequestSpec
        {
            Url = new Uri(baseUri, SessionCookies.CsrfTokenPath).ToString(),
            Headers = [new HeaderEntry("Accept", "application/json")],
            TimeoutSeconds = 30
        };
        var res = await executor.SendAsync(spec, ct);
        if (res.StatusCode is < 200 or >= 300)
            throw new FlowException($"Could not get a CSRF token (HTTP {res.StatusCode}). The session may have expired: paste a new Cookie header on the Cookies page.");
        return ParseToken(res.Body) ?? throw new FlowException("The CSRF token response did not contain a token.");
    }

    public static string? ParseToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String) return NullIfBlank(root.GetString());
            if (root.ValueKind == JsonValueKind.Object)
                foreach (var name in new[] { "token", "requestToken", "csrfToken" })
                    if (root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && NullIfBlank(p.GetString()) is { } t)
                        return t;
        }
        catch (JsonException) { return NullIfBlank(body.Trim().Trim('"')); }
        return null;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
