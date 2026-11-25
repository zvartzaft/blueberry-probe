using Microsoft.AspNetCore.Http;
using ProbeDesk.Core.Flow;
using ProbeDesk.Web.Services;

namespace ProbeDesk.Web.Tests;

public class HelpTests
{
    private static readonly string[] Required =
        ["getting-started", "variables-environments", "cookie-csrf-login", "file-uploads", "import", "secrets", "shortcuts", "whats-new"];

    private readonly HelpService _help = new();

    [Fact]
    public void All_required_pages_exist_and_have_titles()
    {
        foreach (var slug in Required) Assert.NotNull(_help.Find(slug));
        Assert.All(_help.Pages, p => Assert.False(string.IsNullOrWhiteSpace(p.Title), $"{p.Slug} has no title"));
    }

    [Fact]
    public void Every_internal_help_link_resolves()
    {
        foreach (var page in _help.Pages)
            foreach (var slug in HelpService.InternalSlugs(page))
                Assert.True(_help.Find(slug) is not null, $"{page.Slug} links to missing page '{slug}'");
    }

    [Fact]
    public void Rendering_and_search_work()
    {
        Assert.Contains("<h1", HelpService.Render(_help.Find("getting-started")!));
        Assert.Contains(_help.Search("CSRF"), p => p.Slug == "cookie-csrf-login");
    }
}

public class GuardTests
{
    private static async Task<int> Call(string host, string path = "/", string? origin = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        ctx.Request.Path = path;
        if (origin is not null) ctx.Request.Headers.Origin = origin;
        ctx.Response.Body = new MemoryStream();
        var mw = new LocalGuardMiddleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; });
        await mw.InvokeAsync(ctx);
        return ctx.Response.StatusCode;
    }

    [Fact]
    public async Task Rejects_foreign_host_and_origin()
    {
        Assert.Equal(403, await Call("evil.example"));
        Assert.Equal(403, await Call("127.0.0.1:5000", origin: "https://evil.example"));
        Assert.Equal(403, await Call("127.0.0.1:5000", origin: "http://127.0.0.1:9999"));
        Assert.Equal(200, await Call("127.0.0.1:5000", origin: "http://127.0.0.1:5000"));
        Assert.Equal(200, await Call("localhost:5000"));
    }

    [Fact]
    public async Task Echo_still_checks_host()
    {
        Assert.Equal(200, await Call("127.0.0.1:5000", "/_echo/x"));
        Assert.Equal(403, await Call("evil.example", "/_echo/x"));
    }
}

public class ErrorTextTests
{
    [Fact]
    public void Unknown_exceptions_never_leak_details()
    {
        var text = ErrorText.For(new InvalidOperationException("secret-token-123 at C:\\path"));
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("C:\\", text);
    }

    [Fact]
    public void Missing_variables_are_listed()
    =>
        Assert.Contains("a", ErrorText.For(new MissingVariableException(["a", "b"])));
}
