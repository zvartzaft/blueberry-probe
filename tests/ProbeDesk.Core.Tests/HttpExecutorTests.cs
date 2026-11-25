using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProbeDesk.Core.Http;

namespace ProbeDesk.Core.Tests;

public sealed class HttpExecutorTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _base = "";

    public async Task InitializeAsync()
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.ConfigureKestrel(o => o.Listen(System.Net.IPAddress.Loopback, 0));
        _app = b.Build();
        _app.MapMethods("/echo", ["GET", "POST", "PUT", "DELETE", "PATCH", "CUSTOM"], async (HttpContext c) =>
        {
            using var r = new StreamReader(c.Request.Body);
            var body = await r.ReadToEndAsync();
            c.Response.Headers["X-Method"] = c.Request.Method;
            c.Response.Headers["X-Ct"] = c.Request.ContentType ?? "";
            c.Response.Headers["X-Custom"] = c.Request.Headers["X-Custom"].ToString();
            return Results.Text(body);
        });
        _app.MapGet("/setcookie", (HttpContext c) =>
        {
            c.Response.Headers.SetCookie = "sid=abc; Path=/";
            return Results.Redirect("/after");
        });
        _app.MapGet("/after", (HttpContext c) => Results.Text("cookie:" + c.Request.Cookies["sid"]));
        _app.MapGet("/slow", async (HttpContext c) => { await Task.Delay(5000, c.RequestAborted); return "x"; });
        await _app.StartAsync();
        _base = _app.Urls.First();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData("GET")] [InlineData("POST")] [InlineData("PUT")] [InlineData("PATCH")]
    [InlineData("DELETE")] [InlineData("CUSTOM")]
    public async Task Sends_any_verb(string method)
    {
        using var s = new HttpSession();
        var res = await new HttpExecutor(s).SendAsync(new RequestSpec { Method = method, Url = _base + "/echo" },
            default);
        Assert.Equal(200, res.StatusCode);
        Assert.Contains(res.Headers, h => h.Name == "X-Method" && h.Value == method);
    }

    [Fact]
    public async Task Sends_raw_body_with_content_type_and_header()
    {
        using var s = new HttpSession();
        var spec = new RequestSpec
        {
            Method = "POST", Url = _base + "/echo", BodyType = BodyType.Raw, Body = "{\"a\":1}",
            ContentType = "application/json", Headers = [new("X-Custom", "v1")]
        };
        var res = await new HttpExecutor(s).SendAsync(spec, default);
        Assert.Equal("{\"a\":1}", res.Body);
        Assert.Contains(res.Headers, h => h.Name == "X-Ct" && h.Value.StartsWith("application/json"));
        Assert.Contains(res.Headers, h => h.Name == "X-Custom" && h.Value == "v1");
    }

    [Fact]
    public async Task Does_not_follow_redirect_by_default_and_exposes_set_cookie()
    {
        using var s = new HttpSession();
        var res = await new HttpExecutor(s).SendAsync(new RequestSpec { Url = _base + "/setcookie" },
            default);
        Assert.Equal(302, res.StatusCode);
        Assert.Contains(res.SetCookies, c => c.StartsWith("sid=abc"));
    }

    [Fact]
    public async Task Follows_redirect_sharing_cookies()
    {
        using var s = new HttpSession();
        var res = await new HttpExecutor(s).SendAsync(
            new RequestSpec { Url = _base + "/setcookie", FollowRedirects = true },
            default);
        Assert.Equal(200, res.StatusCode);
        Assert.Equal("cookie:abc", res.Body);
        Assert.Equal(1, res.RedirectCount);
    }

    [Fact]
    public async Task Timeout_throws_cancellation()
    {
        using var s = new HttpSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HttpExecutor(s).SendAsync(
            new RequestSpec { Url = _base + "/slow", TimeoutSeconds = 1 }, default));
    }

    [Fact]
    public void Rejects_non_http_url() =>
        Assert.Throws<ArgumentException>(() => RequestBuilder.Build(new RequestSpec { Url = "file:///etc/passwd" }));
}
