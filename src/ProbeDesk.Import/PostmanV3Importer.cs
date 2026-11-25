using System.Text.Json;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Import;

/// <summary>
/// Postman's multi-file YAML format: one file per request (<c>$kind: http-request</c>) plus collection and folder
/// definitions (<c>$kind: collection</c>, usually in <c>.resources/definition.yaml</c>). Scripts are reported, never executed.
/// The zip importer places each request by its path; a single file yields one request or one environment.
/// </summary>
public static class PostmanV3Importer
{
    public static ImportResult Import(JsonElement root, string fileName)
    {
        var result = new ImportResult { PlaceByPath = true };
        var kind = Str(root, "$kind");
        var stem = StripName(fileName);
        switch (kind)
        {
            case "http-request":
                var name = Str(root, "name") is { Length: > 0 } n ? n : stem;
                ReportScripts(root, name, result.Report);
                result.Root = new FolderNode { Name = name };
                result.Root.Requests.Add(ImportRequest(name, root, result.Report));
                break;
            case "collection":
                ImportDefinition(root, stem, result);
                break;
            default:
                result.Report.Warn($"The Postman resource type '{kind}' is not supported and was skipped.");
                break;
        }
        return result;
    }

    private static void ImportDefinition(JsonElement root, string stem, ImportResult result)
    {
        var where = Str(root, "name") is { Length: > 0 } n ? n : stem;
        ReportScripts(root, where, result.Report);
        if (root.TryGetProperty("auth", out var auth) && ParseAuth(auth, result.Report, where) is not null)
            result.Report.Warn($"Auth on '{where}' is not inherited by its requests; set auth on each request.");

        var env = new RequestEnvironment { Name = where + " (variables)" };
        foreach (var v in Items(root, "variables"))
        {
            if (IsDisabled(v)) { result.Report.Info($"Disabled variable '{Str(v, "key")}' was skipped."); continue; }
            env.Variables.Add(new Variable { Name = Str(v, "key"), Value = Str(v, "value"), IsSecret = v.TryGetProperty("secret", out var s) && s.ValueKind == JsonValueKind.True });
        }
        if (env.Variables.Count > 0) result.Environments.Add(env);
    }

    private static SavedRequest ImportRequest(string name, JsonElement req, ImportReport report)
    {
        var spec = new RequestSpec { Method = Str(req, "method") is { Length: > 0 } m ? m.ToUpperInvariant() : "GET", Url = Str(req, "url") };
        var saved = new SavedRequest { Name = name, Spec = spec };

        foreach (var h in Items(req, "headers"))
        {
            if (IsDisabled(h)) { report.Info($"Disabled header '{Str(h, "key")}' in '{name}' was skipped."); continue; }
            spec.Headers.Add(new HeaderEntry(Str(h, "key"), Str(h, "value")));
        }

        if (!spec.Url.Contains('?'))
        {
            var query = Items(req, "queryParams").Where(q => !IsDisabled(q) && Str(q, "key").Length > 0).ToList();
            if (query.Count > 0)
                spec.Url += "?" + string.Join("&", query.Select(q => Uri.EscapeDataString(Str(q, "key")) + "=" + Uri.EscapeDataString(Str(q, "value"))));
        }

        if (req.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object) ImportBody(saved, body, report);
        if (req.TryGetProperty("auth", out var auth) && ParseAuth(auth, report, name) is { } a) spec.Auth = a;
        return saved;
    }

    private static void ImportBody(SavedRequest saved, JsonElement body, ImportReport report)
    {
        var spec = saved.Spec;
        var content = body.TryGetProperty("content", out var c) ? c : default;
        string Text() => content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "";
        switch (Str(body, "type"))
        {
            case "json": Raw("application/json"); break;
            case "xml": Raw("application/xml"); break;
            case "html": Raw("text/html"); break;
            case "javascript": Raw("application/javascript"); break;
            case "text": Raw("text/plain"); break;
            case "urlencoded":
                spec.BodyType = BodyType.FormUrlEncoded;
                foreach (var f in Fields(content)) spec.FormFields.Add(new FormField { Name = Str(f, "key"), Value = Str(f, "value") });
                break;
            case "formdata":
                spec.BodyType = BodyType.Multipart;
                foreach (var f in Fields(content))
                {
                    if (Str(f, "type") == "file")
                    {
                        spec.FormFields.Add(new FormField { Name = Str(f, "key"), IsFile = true });
                        report.Warn($"The file field '{Str(f, "key")}' in '{saved.Name}' has no file; pick the file again.");
                    }
                    else spec.FormFields.Add(new FormField { Name = Str(f, "key"), Value = Str(f, "value") });
                }
                break;
            case "file":
                spec.BodyType = BodyType.Binary;
                spec.ContentType = "application/octet-stream";
                report.Warn($"The file body in '{saved.Name}' has no file; pick the file again.");
                break;
            case "":
                break;
            default:
                report.Warn($"The body type '{Str(body, "type")}' in '{saved.Name}' is not supported.");
                break;
        }

        void Raw(string contentType)
        {
            spec.BodyType = BodyType.Raw;
            spec.Body = Text();
            spec.ContentType = contentType;
        }
    }

    private static AuthSpec? ParseAuth(JsonElement auth, ImportReport report, string where)
    {
        var type = Str(auth, "type");
        string Get(string key)
        {
            if (!auth.TryGetProperty("credentials", out var cr)) return "";
            if (cr.ValueKind == JsonValueKind.Array)
                return cr.EnumerateArray().Where(e => Str(e, "key") == key).Select(e => Str(e, "value")).FirstOrDefault() ?? "";
            return Str(cr, key);
        }
        switch (type)
        {
            case "bearer": return new AuthSpec { Kind = AuthKind.Bearer, Token = Get("token") };
            case "basic": return new AuthSpec { Kind = AuthKind.Basic, Username = Get("username"), Password = Get("password") };
            case "noauth": case "": return null;
            default:
                report.Warn($"The auth type '{type}' ('{where}') is not supported and was not imported.");
                return null;
        }
    }

    private static void ReportScripts(JsonElement el, string where, ImportReport report)
    {
        foreach (var s in Items(el, "scripts"))
            report.Warn($"A script ({Str(s, "type")}) in '{where}' was not imported and is never executed.");
    }

    private static IEnumerable<JsonElement> Fields(JsonElement content) =>
        content.ValueKind == JsonValueKind.Array ? content.EnumerateArray().Where(f => !IsDisabled(f)) : [];

    private static IEnumerable<JsonElement> Items(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static bool IsDisabled(JsonElement f) => f.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static string StripName(string fileName)
    {
        var n = Path.GetFileName(fileName);
        foreach (var suffix in new[] { ".request.yaml", ".request.yml", ".yaml", ".yml", ".json" })
            if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { n = n[..^suffix.Length]; break; }
        if (n.EndsWith(".request", StringComparison.OrdinalIgnoreCase)) n = n[..^".request".Length];
        return n.Length > 0 ? n : "Postman import";
    }
}
