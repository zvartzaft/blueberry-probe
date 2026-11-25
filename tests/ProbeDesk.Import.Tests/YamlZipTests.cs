using System.IO.Compression;
using System.Text;
using Xunit;

namespace ProbeDesk.Import.Tests;

public class YamlZipTests
{
    private const string Yaml = """
    info:
      name: Demo
    variable:
      - key: base
        value: https://api.example.test
    item:
      - name: Login
        request:
          method: POST
          header:
            - key: X-A
              value: "1"
            - key: X-Off
              value: "2"
              disabled: true
          url:
            raw: "{{base}}/login"
    """;

    private const string EnvYaml = """
    name: Test env
    values:
      - key: token
        value: fake-token
        type: secret
      - key: off
        value: x
        enabled: false
    """;

    private static MemoryStream Zip(params (string Name, string Content)[] files)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (n, c) in files)
            {
                using var w = new StreamWriter(z.CreateEntry(n).Open(), new UTF8Encoding(false));
                w.Write(c);
            }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Yaml_collection_maps_like_json()
    {
        var r = PostmanImporter.ImportYaml(Yaml);
        Assert.Equal(1, r.RequestCount);
        var spec = r.Root.Requests.Count > 0 ? r.Root.Requests[0].Spec : r.Root.Folders[0].Requests[0].Spec;
        Assert.Equal("POST", spec.Method);
        Assert.Single(spec.Headers);
    }

    [Fact]
    public void Yaml_environment_keeps_secret_and_skips_disabled()
    {
        var r = PostmanImporter.ImportYaml(EnvYaml);
        var env = Assert.Single(r.Environments);
        var v = Assert.Single(env.Variables);
        Assert.True(v.IsSecret);
    }

    [Fact]
    public void Invalid_yaml_and_aliases_are_reported()
    {
        Assert.Equal(1, PostmanImporter.ImportYaml("a: [unclosed").Report.WarningCount);
        Assert.Equal(1, PostmanImporter.ImportYaml("item: &a [1]\nother: *a").Report.WarningCount);
    }

    [Fact]
    public void Zip_merges_supported_files_and_reports_the_rest()
    {
        using var z = Zip(("c.yaml", Yaml), ("e.json", """{"name":"E","values":[{"key":"a","value":"b"}]}"""),
            ("r.http", "GET https://x.example.test/a\n"), ("notes.txt", "hello"), ("__MACOSX/x.json", "{}"), ("in.zip", "PK"));
        var r = ZipImporter.Import(z, "pack");
        Assert.Equal(2, r.RequestCount);
        Assert.Contains(r.Environments, e => e.Name == "E");
        Assert.Contains(r.Report.Items, i => i.Message.StartsWith("notes.txt"));
        Assert.Contains(r.Report.Items, i => i.Message.StartsWith("in.zip") && i.Message.Contains("nested"));
        Assert.DoesNotContain(r.Report.Items, i => i.Message.Contains("__MACOSX"));
    }

    [Fact]
    public void Postman_v3_multi_file_format_is_placed_by_path()
    {
        const string def = "$kind: collection\nvariables:\n  - key: baseUrl\n    value: /\n  - key: tok\n    secret: true\nscripts:\n  - type: http:beforeRequest\n    code: x\n    language: text/javascript\n";
        const string req = "$kind: http-request\nurl: https://api.example.test/users/me\nmethod: GET\nheaders:\n  - key: X-A\n    value: \"1\"\n";
        var single = PostmanImporter.ImportYaml(def, "definition.yaml");
        Assert.Equal(2, Assert.Single(single.Environments).Variables.Count);
        Assert.Contains(single.Report.Items, i => i.Message.Contains("script"));

        using var z = Zip((".resources/definition.yaml", def), ("Users/Get me.request.yaml", req), ("Users/Sub/Other.request.yaml", req));
        var r = ZipImporter.Import(z, "api");
        Assert.Equal(2, r.RequestCount);
        var users = Assert.Single(r.Root.Folders);
        Assert.Equal("Users", users.Name);
        Assert.Equal("Get me", Assert.Single(users.Requests).Name);
        Assert.Equal("api (variables)", Assert.Single(r.Environments).Name);
    }

    [Fact]
    public void Zip_limits_and_bad_input_are_reported()
    {
        using var many = Zip(Enumerable.Range(0, ZipImporter.MaxEntries + 1).Select(i => ($"f{i}.json", "{}")).ToArray());
        Assert.Equal(0, ZipImporter.Import(many).RequestCount);
        Assert.Equal(1, ZipImporter.Import(new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))).Report.WarningCount);

        using var big = Zip(("big.json", new string(' ', (int)ZipImporter.MaxEntryBytes + 10)), ("ok.http", "GET https://x.example.test\n"));
        var r = ZipImporter.Import(big);
        Assert.Equal(1, r.RequestCount);
        Assert.Contains(r.Report.Items, i => i.Message.StartsWith("big.json"));

        using var bad = Zip(("../evil.json", "{}"));
        Assert.Contains(ZipImporter.Import(bad).Report.Items, i => i.Message.Contains("unsafe"));
    }
}
