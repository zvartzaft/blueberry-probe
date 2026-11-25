using ProbeDesk.Core.Flow;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Core.Tests;

public class SessionCookieTests(TestApi api) : IClassFixture<TestApi>
{
    private const string Pasted = "Cookie: consent=yes; .AspNetCore.Cookies=fake-auth; _ga=GA1.fake; .AspNetCore.Antiforgery.abc=af1";

    [Fact]
    public void Parse_keeps_only_auth_and_antiforgery_cookies()
    {
        var cookies = SessionCookies.Parse(Pasted);
        Assert.Equal([".AspNetCore.Antiforgery.abc", ".AspNetCore.Cookies"], cookies.Select(c => c.Name).Order().ToArray());
        Assert.True(SessionCookies.HasAuthCookie(cookies));
        Assert.Empty(SessionCookies.Parse("consent=yes"));
        Assert.Empty(SessionCookies.Parse(null));
    }

    [Fact]
    public async Task Seeded_cookies_are_sent_and_csrf_token_is_added_to_mutating_requests()
    {
        using var session = new HttpSession();
        var env = new RequestEnvironment { BaseUrl = api.Base, AutoCsrf = true, SessionCookies = SessionCookies.Parse(Pasted) };
        SessionCookies.Seed(session, new Uri(api.Base), env.SessionCookies);

        var step = new SavedRequest { Spec = new RequestSpec { Method = "POST", Url = "/api/mutate" } };
        var res = await new FlowRunner(session, env).RunStepAsync(step);

        Assert.Equal(200, res.Response.StatusCode);
        // The server re-issued the antiforgery cookie; the jar has the new value.
        var snapshot = SessionCookies.Snapshot(session, new Uri(api.Base));
        Assert.Contains(snapshot, c => c.Name == ".AspNetCore.Antiforgery.abc" && c.Value == "af2");
    }

    [Fact]
    public async Task Csrf_token_is_not_added_when_disabled_or_for_other_origins()
    {
        using var session = new HttpSession();
        var env = new RequestEnvironment { BaseUrl = api.Base, AutoCsrf = false, SessionCookies = SessionCookies.Parse(Pasted) };
        SessionCookies.Seed(session, new Uri(api.Base), env.SessionCookies);
        var step = new SavedRequest { Spec = new RequestSpec { Method = "POST", Url = "/api/mutate" } };
        Assert.Equal(400, (await new FlowRunner(session, env).RunStepAsync(step)).Response.StatusCode);

        env.AutoCsrf = true;
        var other = new SavedRequest { Spec = new RequestSpec { Method = "POST", Url = api.AltBase + "/api/mutate" } };
        Assert.Equal(400, (await new FlowRunner(session, env).RunStepAsync(other)).Response.StatusCode);
    }

    [Fact]
    public async Task Expired_session_gives_a_clear_csrf_error()
    {
        using var session = new HttpSession();
        var env = new RequestEnvironment { BaseUrl = api.Base, AutoCsrf = true };
        var step = new SavedRequest { Spec = new RequestSpec { Method = "DELETE", Url = "/api/mutate" } };
        var ex = await Assert.ThrowsAsync<FlowException>(() => new FlowRunner(session, env).RunStepAsync(step));
        Assert.Contains("paste a new Cookie header", ex.Message);
    }

    [Theory]
    [InlineData("\"abc\"", "abc")]
    [InlineData("{\"token\":\"abc\"}", "abc")]
    [InlineData("{\"requestToken\":\"abc\"}", "abc")]
    [InlineData("", null)]
    public void ParseToken_handles_known_shapes(string body, string? expected) =>
        Assert.Equal(expected, CsrfTokenProvider.ParseToken(body));
}
