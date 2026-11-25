using ProbeDesk.Core.Flow;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Core.Tests;

public class FlowTests(TestApi api) : IClassFixture<TestApi>
{
    private static SavedRequest Req(string name, RequestSpec spec, params Extractor[] ex) =>
        new() { Name = name, Spec = spec, Extractors = [.. ex] };

    [Fact]
    public async Task Csrf_login_flow_runs_in_one_click()
    {
        using var session = new HttpSession();
        var env = new RequestEnvironment { BaseUrl = api.Base };

        var fetch = Req("Fetch page", new RequestSpec { Url = "/login" },
            new Extractor { Source = ExtractSource.Body, Kind = ExtractKind.Css, Expression = "input[name=__RequestVerificationToken]@value", TargetVariable = "csrf" });
        var login = Req("Logga in", new RequestSpec
        {
            Method = "POST", Url = "/login", BodyType = BodyType.FormUrlEncoded, FollowRedirects = true,
            FormFields = [new() { Name = "__RequestVerificationToken", Value = "{{csrf}}" }]
        });
        login.RunBefore = [fetch.Id];
        var me = Req("Min sida", new RequestSpec { Url = "/me" });
        me.RunBefore = [login.Id];

        var all = new[] { fetch, login, me }.ToDictionary(r => r.Id);
        var result = await new FlowRunner(session, env).RunAsync(me, id => all.GetValueOrDefault(id));

        Assert.Equal(3, result.Steps.Count);
        Assert.Equal(200, result.Final.Response.StatusCode);
        Assert.Equal("hello", result.Final.Response.Body);
        Assert.Equal("tok123", session.Variables["csrf"]);
    }

    [Fact]
    public async Task Cookie_extractor_reads_set_cookie_and_header_extractor_reads_header()
    {
        using var session = new HttpSession();
        var env = new RequestEnvironment { BaseUrl = api.Base };
        var step = Req("x", new RequestSpec { Url = "/login" },
            new Extractor { Source = ExtractSource.Cookie, Kind = ExtractKind.Name, Expression = "XSRF-TOKEN", TargetVariable = "c" },
            new Extractor { Source = ExtractSource.Header, Kind = ExtractKind.Name, Expression = "content-type", TargetVariable = "h" });
        await new FlowRunner(session, env).RunStepAsync(step);
        Assert.Equal("tok123", session.Variables["c"]);
        Assert.StartsWith("text/html", session.Variables["h"]);
    }

    [Fact]
    public async Task Missing_variable_is_reported_before_sending()
    {
        using var session = new HttpSession();
        var step = Req("x", new RequestSpec { Url = api.Base + "/{{nope}}" });
        var ex = await Assert.ThrowsAsync<MissingVariableException>(() => new FlowRunner(session).RunStepAsync(step));
        Assert.Contains("nope", ex.Names);
    }

    [Fact]
    public void Flow_cycle_is_rejected()
    {
        var a = Req("a", new RequestSpec()); var b = Req("b", new RequestSpec());
        a.RunBefore = [b.Id]; b.RunBefore = [a.Id];
        var all = new[] { a, b }.ToDictionary(r => r.Id);
        Assert.Throws<FlowException>(() => FlowRunner.Plan(a, id => all.GetValueOrDefault(id)));
    }

    [Fact]
    public async Task OAuth2_client_credentials_adds_bearer_token()
    {
        using var session = new HttpSession();
        var step = Req("x", new RequestSpec
        {
            Url = api.Base + "/protected",
            Auth = new AuthSpec { Kind = AuthKind.OAuth2ClientCredentials, TokenUrl = api.Base + "/token", ClientId = "cid", ClientSecret = "sec" }
        });
        var res = await new FlowRunner(session).RunStepAsync(step);
        Assert.Equal(200, res.Response.StatusCode);
        Assert.Single(session.OAuthTokens);
    }

    [Fact]
    public async Task Assertions_evaluate_status_header_and_jsonpath()
    {
        using var session = new HttpSession();
        var step = Req("x", new RequestSpec { Url = api.Base + "/json" });
        step.Assertions =
        [
            new() { Kind = AssertionKind.Status, Expected = "2xx" },
            new() { Kind = AssertionKind.Header, Expression = "Content-Type", Expected = "application/json; charset=utf-8" },
            new() { Kind = AssertionKind.JsonPath, Expression = "$.items[0].id", Expected = "7" },
            new() { Kind = AssertionKind.JsonPath, Expression = "$.name", Expected = "wrong" }
        ];
        var res = await new FlowRunner(session).RunStepAsync(step);
        Assert.Equal([true, true, true, false], res.Assertions.Select(a => a.Passed));
    }

    [Fact]
    public async Task Sequence_continues_after_a_failing_request()
    {
        using var session = new HttpSession();
        var bad = Req("bad", new RequestSpec { Url = "not a url" });
        var good = Req("good", new RequestSpec { Url = api.Base + "/json" });
        var items = await new FlowRunner(session).RunSequenceAsync([bad, good], _ => null);
        Assert.NotNull(items[0].Error);
        Assert.Equal(200, items[1].Final!.Response.StatusCode);
    }

    [Fact]
    public async Task Credentials_are_not_forwarded_to_another_host()
    {
        using var session = new HttpSession();
        var spec = new RequestSpec
        {
            Url = api.Base + "/other-host", FollowRedirects = true,
            QueryParams = [new("to", api.AltBase + "/auth-echo")],
            Auth = new AuthSpec { Kind = AuthKind.Bearer, Token = "fake-secret" }
        };
        var res = await new HttpExecutor(session).SendAsync(spec);
        Assert.Equal("", res.Body);
    }

    [Fact]
    public async Task Allowed_hosts_block_other_targets()
    {
        using var session = new HttpSession();
        var exec = new HttpExecutor(session, ["only.example.test"]);
        await Assert.ThrowsAsync<BlockedHostException>(() => exec.SendAsync(new RequestSpec { Url = api.Base + "/json" }));
    }

    [Fact]
    public async Task Large_response_is_truncated_and_spooled_to_file()
    {
        using var session = new HttpSession();
        var dir = Path.Combine(Path.GetTempPath(), "pd-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var res = await new HttpExecutor(session, null, dir).SendAsync(new RequestSpec { Url = api.Base + "/big" });
            Assert.True(res.BodyTruncated);
            Assert.Equal(3L * 1024 * 1024, res.SizeBytes);
            Assert.Equal(res.SizeBytes, new FileInfo(res.SpoolPath!).Length);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Multipart_with_file_and_binary_body_are_sent()
    {
        using var session = new HttpSession();
        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "hello-file");
        try
        {
            var mp = await new HttpExecutor(session).SendAsync(new RequestSpec
            {
                Method = "POST", Url = api.Base + "/upload", BodyType = BodyType.Multipart,
                FormFields =
                [
                    new() { Name = "note", Value = "hi" },
                    new() { Name = "f", IsFile = true, FilePath = file, FileName = "a.txt", MimeType = "text/plain" }
                ]
            });
            var j = TestApi.Json(mp.Body);
            Assert.Equal("a.txt", j.GetProperty("name").GetString());
            Assert.Equal(10, j.GetProperty("length").GetInt32());
            Assert.Equal("hi", j.GetProperty("note").GetString());

            var bin = await new HttpExecutor(session).SendAsync(new RequestSpec
            {
                Method = "PUT", Url = api.Base + "/bin", BodyType = BodyType.Binary, BinaryFilePath = file, ContentType = "application/octet-stream"
            });
            Assert.Equal(10, TestApi.Json(bin.Body).GetProperty("length").GetInt32());
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Cookie_view_can_list_and_delete()
    {
        using var session = new HttpSession();
        await new HttpExecutor(session).SendAsync(new RequestSpec { Url = api.Base + "/login" });
        var c = Assert.Single(CookieView.List(session.Cookies));
        Assert.Equal("XSRF-TOKEN", c.Name);
        CookieView.Delete(session.Cookies, c.Domain, c.Path, c.Name);
        Assert.Empty(CookieView.List(session.Cookies));
    }
}

public class PureLogicTests
{
    [Fact]
    public void Resolver_substitutes_and_prefixes_base_url()
    {
        var spec = new RequestSpec { Url = "/a/{{id}}", Headers = [new("X", "{{t}}")] };
        var r = VariableResolver.Resolve(spec, new Dictionary<string, string> { ["id"] = "5", ["t"] = "v" }, "https://h.example.test/");
        Assert.Equal("https://h.example.test/a/5", r.Url);
        Assert.Equal("v", r.Headers[0].Value);
    }

    [Fact]
    public void Masked_map_hides_secrets()
    {
        var env = new RequestEnvironment { Variables = [new() { Name = "k", Value = "real", IsSecret = true }, new() { Name = "p", Value = "pub" }] };
        var m = VariableResolver.ToMaskedMap(env);
        Assert.Equal("***", m["k"]);
        Assert.Equal("pub", m["p"]);
    }

    [Fact]
    public void BaseUrl_setting_fills_baseUrl_variable_unless_defined()
    {
        var env = new RequestEnvironment { BaseUrl = "https://h.example.test/" };
        Assert.Equal("https://h.example.test", VariableResolver.ToMap(env)["baseUrl"]);
        env.Variables.Add(new() { Name = "baseUrl", Value = "https://other.example.test" });
        Assert.Equal("https://other.example.test", VariableResolver.ToMap(env)["baseUrl"]);
    }

    [Theory]
    [InlineData("$.a.b", "1")]
    [InlineData("$['a']['b']", "1")]
    [InlineData("$.l[1]", "y")]
    [InlineData("$.none", null)]
    public void JsonPath_selects(string path, string? expected) =>
        Assert.Equal(expected, JsonPathLite.Select("{\"a\":{\"b\":1},\"l\":[\"x\",\"y\"]}", path));

    [Fact]
    public void Regex_extractor_uses_first_group()
    {
        var res = new ResponseResult { Body = "csrf=\"abc123\";" };
        var ex = new Extractor { Source = ExtractSource.Body, Kind = ExtractKind.Regex, Expression = "csrf=\"([a-z0-9]+)\"" };
        Assert.Equal("abc123", ExtractorEngine.Extract(ex, res));
    }

    [Fact]
    public void Diff_marks_added_and_removed_lines()
    {
        var d = TextDiff.Diff("a\nb\nc", "a\nc\nd");
        Assert.Contains(new DiffLine(DiffKind.Removed, "b"), d);
        Assert.Contains(new DiffLine(DiffKind.Added, "d"), d);
        Assert.Contains(new DiffLine(DiffKind.Same, "a"), d);
    }

    [Fact]
    public void Session_manager_expires_idle_sessions_and_recreates_on_ssl_change()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using var m = new SessionManager(TimeSpan.FromMinutes(10), time);
        var id = Guid.NewGuid();
        var s1 = m.GetOrCreate(id);
        Assert.Same(s1, m.GetOrCreate(id));
        Assert.NotSame(s1, m.GetOrCreate(id, ignoreSsl: true));
        var s2 = m.GetOrCreate(id, true);
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.NotSame(s2, m.GetOrCreate(id, true));
    }

    [Theory]
    [InlineData("api.example.test", true)]
    [InlineData("a.corp.example.test", true)]
    [InlineData("evil.test", false)]
    public void Host_policy_matches(string host, bool allowed) =>
        Assert.Equal(allowed, HostPolicy.IsAllowed(["api.example.test", "*.corp.example.test"], host));
}
