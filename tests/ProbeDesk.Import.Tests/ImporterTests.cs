using ProbeDesk.Core.Http;
using ProbeDesk.Import;
using Xunit;

namespace ProbeDesk.Import.Tests;

public class ImporterTests
{
    private const string Postman = """
    {"info":{"name":"Demo"},
     "variable":[{"key":"base","value":"https://api.example.test"}],
     "item":[{"name":"Auth","item":[
       {"name":"Login","event":[{"listen":"test","script":{"exec":["pm.test()"]}}],
        "request":{"method":"POST","header":[{"key":"X-A","value":"1"},{"key":"X-Off","value":"2","disabled":true}],
        "url":{"raw":"{{base}}/login"},
        "body":{"mode":"urlencoded","urlencoded":[{"key":"u","value":"demo"}]}}},
       {"name":"Upload","request":{"method":"PUT","url":"https://x.test/up",
        "auth":{"type":"bearer","bearer":[{"key":"token","value":"fake-token"}]},
        "body":{"mode":"formdata","formdata":[{"key":"f","type":"file","src":"a.txt"}]}}},
       {"name":"Odd","request":{"method":"GET","url":"https://x.test","auth":{"type":"oauth1"}}}]}]}
    """;

    [Fact]
    public void Postman_maps_structure_and_reports_untranslated()
    {
        var r = PostmanImporter.Import(Postman);
        Assert.Equal(3, r.RequestCount);
        var login = r.Root.Folders[0].Requests[0].Spec;
        Assert.Equal("POST", login.Method);
        Assert.Single(login.Headers);
        Assert.Equal(BodyType.FormUrlEncoded, login.BodyType);
        Assert.Equal(AuthKind.Bearer, r.Root.Folders[0].Requests[1].Spec.Auth.Kind);
        Assert.Contains(r.Report.Items, i => i.Message.Contains("script"));
        Assert.Contains(r.Report.Items, i => i.Message.Contains("oauth1"));
        Assert.Contains(r.Report.Items, i => i.Message.Contains("has no file"));
        Assert.Single(r.Environments);
    }

    [Fact]
    public void Postman_environment_marks_secrets()
    {
        var r = PostmanImporter.Import("""{"name":"E","values":[{"key":"k","value":"v","type":"secret"},{"key":"p","value":"1"}]}""");
        Assert.True(r.Environments[0].Variables[0].IsSecret);
        Assert.False(r.Environments[0].Variables[1].IsSecret);
    }

    [Fact]
    public void Postman_invalid_json_is_reported()
    {
        var r = PostmanImporter.Import("nope");
        Assert.Equal(0, r.RequestCount);
        Assert.Equal(1, r.Report.WarningCount);
    }

    [Fact]
    public void HttpFile_parses_blocks_vars_and_reports_scripts()
    {
        var text = "@host = https://x.test\n### First\n# @name login\nPOST {{host}}/login HTTP/1.1\nContent-Type: application/json\n\n{\"a\":1}\n\n> {% client.global.set('a','b'); %}\n\n###\nGET {{host}}/n\n  ?q=1\n  &r={{$guid}}\n";
        var r = HttpFileParser.Parse(text, "demo");
        Assert.Equal(2, r.RequestCount);
        Assert.Equal("login", r.Root.Requests[0].Name);
        Assert.Equal("{\"a\":1}", r.Root.Requests[0].Spec.Body);
        Assert.Equal("{{host}}/n?q=1&r={{$guid}}", r.Root.Requests[1].Spec.Url);
        Assert.Equal("host", r.Environments[0].Variables[0].Name);
        Assert.Contains(r.Report.Items, i => i.Message.Contains("Response handlers"));
        Assert.Contains(r.Report.Items, i => i.Message.Contains("$guid"));
    }

    [Fact]
    public void HttpFile_file_body_becomes_binary()
    {
        var r = HttpFileParser.Parse("PUT https://x.test/up\nContent-Type: image/png\n\n< ./a.png\n");
        var s = r.Root.Requests[0].Spec;
        Assert.Equal(BodyType.Binary, s.BodyType);
        Assert.Equal("", s.BinaryFilePath); // untrusted paths are never imported
    }

    [Fact]
    public void Curl_parses_common_options()
    {
        var (req, report) = CurlParser.Parse("curl -X POST 'https://x.test/a' -H 'X-T: 1' -u u:p -d '{\"a\":1}' -H 'Content-Type: application/json' -k --weird");
        Assert.NotNull(req);
        Assert.Equal("POST", req!.Spec.Method);
        Assert.Equal(AuthKind.Basic, req.Spec.Auth.Kind);
        Assert.Equal("application/json", req.Spec.ContentType);
        Assert.Equal(2, report.WarningCount);
    }

    [Fact]
    public void Curl_form_and_non_curl()
    {
        var (req, _) = CurlParser.Parse("curl https://x.test -F a=b -F f=@./x.txt");
        Assert.Equal(BodyType.Multipart, req!.Spec.BodyType);
        Assert.True(req.Spec.FormFields[1].IsFile);
        Assert.Null(CurlParser.Parse("wget x").Request);
    }
}
