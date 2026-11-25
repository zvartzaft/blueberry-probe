using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace ProbeDesk.Core.Tests;

/// <summary>Loopback server with fake endpoints for login/CSRF, OAuth2, uploads and redirects.</summary>
public sealed class TestApi : IAsyncLifetime
{
    private WebApplication _app = null!;
    public string Base { get; private set; } = "";
    public string AltBase => Base.Replace("127.0.0.1", "localhost");

    public async Task InitializeAsync()
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.ConfigureKestrel(o => o.Listen(System.Net.IPAddress.Loopback, 0));
        _app = b.Build();

        _app.MapGet("/login", (HttpContext c) =>
        {
            c.Response.Headers.SetCookie = "XSRF-TOKEN=tok123; Path=/";
            return Results.Content("<html><form><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"tok123\" /></form></html>", "text/html");
        });
        _app.MapPost("/login", async (HttpContext c) =>
        {
            var form = await c.Request.ReadFormAsync();
            var token = form["__RequestVerificationToken"].ToString();
            if (token.Length == 0) token = c.Request.Headers["X-XSRF-TOKEN"].ToString();
            if (token != c.Request.Cookies["XSRF-TOKEN"] || token.Length == 0) return Results.StatusCode(400);
            c.Response.Headers.SetCookie = "auth=ok; Path=/";
            return Results.Redirect("/me");
        });
        _app.MapGet("/me", (HttpContext c) => c.Request.Cookies["auth"] == "ok" ? Results.Text("hello") : Results.StatusCode(401));
        _app.MapPost("/token", async (HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync();
            return f["client_id"] == "cid" && f["client_secret"] == "sec" && f["grant_type"] == "client_credentials"
                ? Results.Json(new { access_token = "t1", expires_in = 3600 }) : Results.StatusCode(401);
        });
        _app.MapGet("/protected", (HttpContext c) =>
            c.Request.Headers.Authorization == "Bearer t1" ? Results.Text("ok") : Results.StatusCode(401));
        _app.MapPost("/upload", async (HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync();
            var file = f.Files.FirstOrDefault();
            return Results.Json(new { name = file?.FileName, length = file?.Length, ct = file?.ContentType, note = f["note"].ToString() });
        });
        _app.MapPut("/bin", async (HttpContext c) =>
        {
            using var ms = new MemoryStream();
            await c.Request.Body.CopyToAsync(ms);
            return Results.Json(new { length = ms.Length, ct = c.Request.ContentType });
        });
        _app.MapGet("/auth-echo", (HttpContext c) => Results.Text(c.Request.Headers.Authorization.ToString()));
        _app.MapGet("/other-host", (HttpContext c) =>
            Results.Redirect(c.Request.Query["to"].ToString()));
        _app.MapGet("/json", () => Results.Json(new { items = new[] { new { id = 7 } }, name = "x" }));
        _app.MapGet("/api/authentication/antiforgery/token", (HttpContext c) =>
        {
            if (c.Request.Cookies[".AspNetCore.Cookies"] != "fake-auth") return Results.StatusCode(401);
            c.Response.Headers.SetCookie = ".AspNetCore.Antiforgery.abc=af2; Path=/";
            return Results.Json("csrf1");
        });
        _app.MapPost("/api/mutate", (HttpContext c) =>
            c.Request.Cookies[".AspNetCore.Cookies"] == "fake-auth" && c.Request.Headers["X-CSRF-TOKEN"] == "csrf1"
                ? Results.Text("done") : Results.StatusCode(400));
        _app.MapGet("/big", async (HttpContext c) =>
        {
            c.Response.ContentType = "text/plain";
            var chunk = new string('a', 1024);
            for (var i = 0; i < 3 * 1024; i++) await c.Response.WriteAsync(chunk);
        });
        await _app.StartAsync();
        Base = _app.Urls.First();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    public static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;
}
