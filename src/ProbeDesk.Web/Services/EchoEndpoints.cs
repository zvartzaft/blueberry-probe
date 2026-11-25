using System.Net;
using System.Text;
using System.Text.Json;

namespace ProbeDesk.Web.Services;

/// <summary>Built-in local echo and login/CSRF demo so users can practise without touching real systems.</summary>
public static class EchoEndpoints
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
        { "Authorization", "Cookie", "Set-Cookie", "Proxy-Authorization", "X-Api-Key" };
    private const int MaxBody = 64 * 1024;

    public static void MapEcho(this IEndpointRouteBuilder app)
    {
        app.MapGet("/_echo/login-page", (HttpContext ctx) =>
        {
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            ctx.Response.Cookies.Append("XSRF-TOKEN", token, new CookieOptions { SameSite = SameSiteMode.Strict, Path = "/_echo" });
            var html = $"<!DOCTYPE html><html><body><form method=\"post\" action=\"/_echo/login\">" +
                       $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{token}\" />" +
                       "<input name=\"username\" /><input name=\"password\" type=\"password\" /></form></body></html>";
            return Results.Content(html, "text/html; charset=utf-8");
        }).DisableAntiforgery();

        app.MapPost("/_echo/login", async (HttpContext ctx) =>
        {
            var cookie = ctx.Request.Cookies["XSRF-TOKEN"];
            string? sent = ctx.Request.Headers["X-XSRF-TOKEN"].FirstOrDefault() ?? ctx.Request.Headers["RequestVerificationToken"].FirstOrDefault();
            if (sent is null && ctx.Request.HasFormContentType)
                sent = (await ctx.Request.ReadFormAsync())["__RequestVerificationToken"].FirstOrDefault();
            if (string.IsNullOrEmpty(cookie) || cookie != sent)
                return Results.Json(new { error = "Invalid or missing CSRF token." }, statusCode: 400);
            ctx.Response.Cookies.Append("echo_auth", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/_echo" });
            return Results.Redirect("/_echo/me");
        }).DisableAntiforgery();

        app.MapGet("/_echo/me", (HttpContext ctx) =>
            ctx.Request.Cookies.ContainsKey("echo_auth")
                ? Results.Json(new { loggedIn = true, message = "Login succeeded." })
                : Results.Json(new { loggedIn = false }, statusCode: 401)).DisableAntiforgery();

        app.Map("/_echo/{**rest}", async (HttpContext ctx) =>
        {
            var req = ctx.Request;
            string body = "";
            long size = 0;
            var files = new List<object>();
            if (req.HasFormContentType && req.ContentType!.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                var form = await req.ReadFormAsync();
                foreach (var f in form.Files) files.Add(new { field = f.Name, fileName = f.FileName, size = f.Length, contentType = f.ContentType });
                body = JsonSerializer.Serialize(form.ToDictionary(k => k.Key, v => v.Value.ToString()));
            }
            else
            {
                var buf = new byte[MaxBody];
                var n = 0; int r;
                while (n < buf.Length && (r = await req.Body.ReadAsync(buf.AsMemory(n))) > 0) n += r;
                size = n;
                body = Encoding.UTF8.GetString(buf, 0, n);
            }
            return Results.Json(new
            {
                method = req.Method,
                path = req.Path.Value,
                query = req.Query.ToDictionary(k => k.Key, v => v.Value.ToString()),
                headers = req.Headers.ToDictionary(h => h.Key, h => Sensitive.Contains(h.Key) ? "***" : h.Value.ToString()),
                cookies = req.Cookies.Keys.ToArray(),
                bodySize = size,
                body,
                files
            });
        }).DisableAntiforgery();
    }
}
