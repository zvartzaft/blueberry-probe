using System.Text.Json;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Import;

/// <summary>Postman Collection v2.1 and Postman environment files. Scripts are reported, never executed.</summary>
public static class PostmanImporter
{
    public static ImportResult Import(string json, string fileName = "")
    {
        var result = new ImportResult();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            result.Report.Warn("The file is not valid JSON.");
            return result;
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.Report.Warn("Unknown file format: expected a JSON object.");
                return result;
            }
            if (root.TryGetProperty("$kind", out _)) return PostmanV3Importer.Import(root, fileName);
            if (root.TryGetProperty("item", out _)) ImportCollection(root, result);
            else if (root.TryGetProperty("values", out _)) ImportEnvironment(root, result);
            else result.Report.Warn("The file looks like neither a Postman collection (item) nor an environment (values).");
        }
        return result;
    }

    public static ImportResult ImportYaml(string yaml, string fileName = "")
    {
        try { return Import(YamlToJson.Convert(yaml), fileName); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException)
        {
            var result = new ImportResult();
            result.Report.Warn(ex is FormatException ? ex.Message : "The YAML content does not look like a Postman collection or environment.");
            return result;
        }
    }

    private static void ImportEnvironment(JsonElement root, ImportResult result)
    {
        var env = new RequestEnvironment { Name = Str(root, "name") is { Length: > 0 } n ? n : "Postman environment" };
        foreach (var v in root.GetProperty("values").EnumerateArray())
        {
            if (v.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False)
            {
                result.Report.Info($"Disabled variable '{Str(v, "key")}' was skipped.");
                continue;
            }
            env.Variables.Add(new Variable { Name = Str(v, "key"), Value = Str(v, "value"), IsSecret = Str(v, "type") == "secret" });
        }
        result.Environments.Add(env);
    }

    private static void ImportCollection(JsonElement root, ImportResult result)
    {
        var name = root.TryGetProperty("info", out var info) ? Str(info, "name") : "";
        result.Root = new FolderNode { Name = name.Length > 0 ? name : "Postman-import" };
        ReportEvents(root, result.Root.Name, result.Report);

        var inherited = root.TryGetProperty("auth", out var a) ? ParseAuth(a, result.Report, result.Root.Name) : null;
        ImportItems(root.GetProperty("item"), result.Root, inherited, result);

        if (root.TryGetProperty("variable", out var vars) && vars.ValueKind == JsonValueKind.Array)
        {
            var env = new RequestEnvironment { Name = result.Root.Name + " (variables)" };
            foreach (var v in vars.EnumerateArray())
                env.Variables.Add(new Variable { Name = Str(v, "key"), Value = Str(v, "value") });
            if (env.Variables.Count > 0) result.Environments.Add(env);
        }
    }

    private static void ImportItems(JsonElement items, FolderNode parent, AuthSpec? inherited, ImportResult result)
    {
        foreach (var item in items.EnumerateArray())
        {
            var name = Str(item, "name");
            ReportEvents(item, name, result.Report);
            var auth = inherited;
            if (item.TryGetProperty("auth", out var ia)) auth = ParseAuth(ia, result.Report, name) ?? auth;

            if (item.TryGetProperty("item", out var children))
            {
                var folder = new FolderNode { Name = name };
                parent.Folders.Add(folder);
                ImportItems(children, folder, auth, result);
            }
            else if (item.TryGetProperty("request", out var req))
            {
                parent.Requests.Add(ImportRequest(name, req, auth, result.Report));
            }
        }
    }

    private static SavedRequest ImportRequest(string name, JsonElement req, AuthSpec? inherited, ImportReport report)
    {
        var spec = new RequestSpec();
        var saved = new SavedRequest { Name = name.Length > 0 ? name : "Imported request", Spec = spec };

        if (req.ValueKind == JsonValueKind.String) { spec.Url = req.GetString() ?? ""; return saved; }

        spec.Method = Str(req, "method") is { Length: > 0 } m ? m.ToUpperInvariant() : "GET";
        if (req.TryGetProperty("url", out var url))
        {
            if (url.ValueKind == JsonValueKind.String) spec.Url = url.GetString() ?? "";
            else
            {
                spec.Url = Str(url, "raw");
                if (url.TryGetProperty("variable", out var pv) && pv.ValueKind == JsonValueKind.Array && pv.GetArrayLength() > 0)
                    report.Warn($"Path variables (:name) in '{saved.Name}' are not imported; replace them with {{{{variable}}}}.");
            }
        }

        if (req.TryGetProperty("header", out var headers) && headers.ValueKind == JsonValueKind.Array)
            foreach (var h in headers.EnumerateArray())
            {
                if (h.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)
                {
                    report.Info($"Disabled header '{Str(h, "key")}' in '{saved.Name}' was skipped.");
                    continue;
                }
                spec.Headers.Add(new HeaderEntry(Str(h, "key"), Str(h, "value")));
            }

        if (req.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
            ImportBody(saved, body, report);

        if (req.TryGetProperty("auth", out var auth)) spec.Auth = ParseAuth(auth, report, saved.Name) ?? inherited ?? new();
        else if (inherited is not null) spec.Auth = inherited;
        return saved;
    }

    private static void ImportBody(SavedRequest saved, JsonElement body, ImportReport report)
    {
        var spec = saved.Spec;
        switch (Str(body, "mode"))
        {
            case "raw":
                spec.BodyType = BodyType.Raw;
                spec.Body = Str(body, "raw");
                var lang = body.TryGetProperty("options", out var o) && o.TryGetProperty("raw", out var r) ? Str(r, "language") : "";
                spec.ContentType = lang switch { "json" => "application/json", "xml" => "application/xml", "html" => "text/html", _ => "text/plain" };
                break;
            case "urlencoded":
                spec.BodyType = BodyType.FormUrlEncoded;
                foreach (var f in Items(body, "urlencoded").Where(f => !IsDisabled(f)))
                    spec.FormFields.Add(new FormField { Name = Str(f, "key"), Value = Str(f, "value") });
                break;
            case "formdata":
                spec.BodyType = BodyType.Multipart;
                foreach (var f in Items(body, "formdata").Where(f => !IsDisabled(f)))
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
            case "graphql":
                spec.BodyType = BodyType.Raw;
                spec.ContentType = "application/json";
                if (body.TryGetProperty("graphql", out var g)) spec.Body = g.GetRawText();
                report.Warn($"The GraphQL body in '{saved.Name}' was imported as raw JSON.");
                break;
            case "":
                break;
            default:
                report.Warn($"The body mode '{Str(body, "mode")}' in '{saved.Name}' is not supported.");
                break;
        }
    }

    private static AuthSpec? ParseAuth(JsonElement auth, ImportReport report, string where)
    {
        var type = Str(auth, "type");
        string Get(string key) => auth.TryGetProperty(type, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(e => Str(e, "key") == key).Select(e => Str(e, "value")).FirstOrDefault() ?? "" : "";
        switch (type)
        {
            case "bearer": return new AuthSpec { Kind = AuthKind.Bearer, Token = Get("token") };
            case "basic": return new AuthSpec { Kind = AuthKind.Basic, Username = Get("username"), Password = Get("password") };
            case "noauth": return new AuthSpec();
            case "": return null;
            default:
                report.Warn($"The auth type '{type}' ('{where}') is not supported and was not imported.");
                return null;
        }
    }

    private static void ReportEvents(JsonElement el, string where, ImportReport report)
    {
        if (!el.TryGetProperty("event", out var events) || events.ValueKind != JsonValueKind.Array) return;
        foreach (var e in events.EnumerateArray())
            report.Warn($"A script ({Str(e, "listen")}) in '{where}' was not imported and is never executed.");
    }

    private static IEnumerable<JsonElement> Items(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static bool IsDisabled(JsonElement f) => f.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}
