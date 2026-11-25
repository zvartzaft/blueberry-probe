using System.Security.Cryptography;
using System.Text;

namespace ProbeDesk.Web.Services;

/// <summary>
/// Defends the local port against malicious web pages (CSRF, DNS rebinding) by validating Host and Origin.
/// </summary>
public sealed class LocalGuardMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowedHostNames = ["localhost", "127.0.0.1", "[::1]"];

    public static bool IsAllowedHost(string host) => AllowedHostNames.Contains(host, StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!IsAllowedHost(ctx.Request.Host.Host)) { await Deny(ctx); return; }

        if (ctx.Request.Headers.Origin.FirstOrDefault() is { } origin)
        {
            var ok = Uri.TryCreate(origin, UriKind.Absolute, out var o)
                     && IsAllowedHost(o.Host.StartsWith(':') ? o.Host : o.IdnHost.Contains(':') ? $"[{o.IdnHost}]" : o.IdnHost)
                     && o.Port == (ctx.Request.Host.Port ?? (ctx.Request.IsHttps ? 443 : 80));
            if (!ok) { await Deny(ctx); return; }
        }

        await next(ctx);
    }

    private static Task Deny(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        return ctx.Response.WriteAsync("Access denied.");
    }
}
