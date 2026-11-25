using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Import.Export;

public sealed record ExportResult(string Content, IReadOnlyList<string> Warnings);

/// <summary>Exports collections. Secrets are stripped by default; token-like content raises a warning.</summary>
public static partial class Exporter
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Authorization", "Cookie", "Set-Cookie", "Proxy-Authorization", "X-Api-Key", "X-Auth-Token" };

    [GeneratedRegex(@"eyJ[\w-]{10,}\.[\w-]{10,}\.[\w-]*|(?i:bearer\s+[\w\-.~+/]{16,})|(?i:(?:api[_-]?key|secret|token|password)""?\s*[:=]\s*""?[\w\-.~+/]{8,})")]
    private static partial Regex TokenLike();

    private static string Clean(string value, bool strip, string where, List<string> warnings)
    {
        if (TokenLike().IsMatch(value))
        {
            warnings.Add($"A possible secret was found in {where}.");
            if (strip) return "{{REDACTED}}";
        }
        return value;
    }

    private static List<HeaderEntry> SafeHeaders(RequestSpec s, bool strip, string name, List<string> warnings) =>
        s.Headers.Select(h =>
        {
            if (SensitiveHeaders.Contains(h.Name))
            {
                warnings.Add($"Header '{h.Name}' in '{name}' contains sensitive values.");
                return strip ? h with { Value = "{{" + h.Name.Replace('-', '_').ToLowerInvariant() + "}}" } : h;
            }
            return h with { Value = Clean(h.Value, strip, $"header '{h.Name}' in '{name}'", warnings) };
        }).ToList();

    public static ExportResult ToPostman(FolderNode root, bool stripSecrets = true)
    {
        var warnings = new List<string>();
        var items = Items(root, stripSecrets, warnings);
        var doc = new Dictionary<string, object>
        {
            ["info"] = new Dictionary<string, object>
            {
                ["name"] = root.Name.Length > 0 ? root.Name : "Blueberry Probe",
                ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
            },
            ["item"] = items
        };
        return new(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }), Distinct(warnings));
    }

    private static List<object> Items(FolderNode node, bool strip, List<string> warnings)
    {
        var list = new List<object>();
        foreach (var f in node.Folders)
            list.Add(new Dictionary<string, object> { ["name"] = f.Name, ["item"] = Items(f, strip, warnings) });
        foreach (var r in node.Requests)
            list.Add(new Dictionary<string, object> { ["name"] = r.Name, ["request"] = PostmanRequest(r, strip, warnings) });
        return list;
    }

    private static Dictionary<string, object> PostmanRequest(SavedRequest r, bool strip, List<string> warnings)
    {
        var s = r.Spec;
        var req = new Dictionary<string, object>
        {
            ["method"] = s.Method,
            ["header"] = SafeHeaders(s, strip, r.Name, warnings).Select(h => new { key = h.Name, value = h.Value }).ToList(),
            ["url"] = new { raw = Clean(s.Url, strip, $"the URL of '{r.Name}'", warnings) }
        };
        switch (s.BodyType)
        {
            case BodyType.Raw:
                req["body"] = new { mode = "raw", raw = Clean(s.Body, strip, $"the body of '{r.Name}'", warnings) };
                break;
            case BodyType.FormUrlEncoded:
                req["body"] = new { mode = "urlencoded", urlencoded = s.FormFields.Select(f => new { key = f.Name, value = Clean(f.Value, strip, $"field '{f.Name}' in '{r.Name}'", warnings) }).ToList() };
                break;
            case BodyType.Multipart:
                req["body"] = new
                {
                    mode = "formdata",
                    formdata = s.FormFields.Select(f => f.IsFile
                        ? (object)new { key = f.Name, type = "file" }
                        : new { key = f.Name, type = "text", value = Clean(f.Value, strip, $"field '{f.Name}' in '{r.Name}'", warnings) }).ToList()
                };
                break;
            case BodyType.Binary:
                warnings.Add($"The file body of '{r.Name}' is not exported to Postman; pick the file again after importing.");
                break;
        }
        switch (s.Auth.Kind)
        {
            case AuthKind.Bearer:
                warnings.Add($"Authentication in '{r.Name}' contains sensitive values.");
                req["auth"] = new { type = "bearer", bearer = new[] { new { key = "token", value = strip ? "{{token}}" : s.Auth.Token, type = "string" } } };
                break;
            case AuthKind.Basic:
                warnings.Add($"Authentication in '{r.Name}' contains sensitive values.");
                req["auth"] = new
                {
                    type = "basic",
                    basic = new[]
                    {
                        new { key = "username", value = strip ? "{{username}}" : s.Auth.Username, type = "string" },
                        new { key = "password", value = strip ? "{{password}}" : s.Auth.Password, type = "string" }
                    }
                };
                break;
            case AuthKind.OAuth2ClientCredentials:
                warnings.Add($"OAuth2 settings in '{r.Name}' are not exported (not supported by this format).");
                break;
        }
        return req;
    }

    public static ExportResult ToHttpFile(FolderNode root, bool stripSecrets = true)
    {
        var warnings = new List<string>();
        var sb = new StringBuilder();
        Write(root, sb, stripSecrets, warnings);
        return new(sb.ToString(), Distinct(warnings));
    }

    private static void Write(FolderNode node, StringBuilder sb, bool strip, List<string> warnings)
    {
        foreach (var f in node.Folders) Write(f, sb, strip, warnings);
        foreach (var r in node.Requests)
        {
            var s = r.Spec;
            sb.Append("### ").AppendLine(r.Name.ReplaceLineEndings(" "));
            sb.Append("# @name ").AppendLine(Regex.Replace(r.Name, @"\W+", "_").Trim('_'));
            sb.Append(s.Method).Append(' ').AppendLine(Clean(s.Url, strip, $"the URL of '{r.Name}'", warnings));
            foreach (var h in SafeHeaders(s, strip, r.Name, warnings)) sb.Append(h.Name).Append(": ").AppendLine(h.Value);
            if (s.Auth.Kind is AuthKind.Bearer)
            {
                warnings.Add($"Authentication in '{r.Name}' contains sensitive values.");
                sb.Append("Authorization: Bearer ").AppendLine(strip ? "{{token}}" : s.Auth.Token);
            }
            else if (s.Auth.Kind is AuthKind.Basic)
            {
                warnings.Add($"Authentication in '{r.Name}' contains sensitive values.");
                sb.Append("Authorization: Basic ").AppendLine(strip ? "{{basic_credentials}}"
                    : Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Auth.Username + ":" + s.Auth.Password)));
            }
            switch (s.BodyType)
            {
                case BodyType.Raw:
                    sb.Append("Content-Type: ").AppendLine(s.ContentType).AppendLine().AppendLine(Clean(s.Body, strip, $"the body of '{r.Name}'", warnings));
                    break;
                case BodyType.FormUrlEncoded:
                    sb.AppendLine("Content-Type: application/x-www-form-urlencoded").AppendLine()
                      .AppendLine(string.Join("&", s.FormFields.Select(f => $"{Esc(f.Name)}={Esc(Clean(f.Value, strip, $"field '{f.Name}' in '{r.Name}'", warnings))}")));
                    break;
                case BodyType.Binary:
                    sb.Append("Content-Type: ").AppendLine(s.ContentType).AppendLine().AppendLine("< ./" + Path.GetFileName(s.BinaryFilePath));
                    break;
                case BodyType.Multipart:
                    warnings.Add($"The multipart body of '{r.Name}' cannot be expressed in .http and was omitted.");
                    break;
            }
            sb.AppendLine();
        }
    }

    private static string Esc(string s) => Uri.EscapeDataString(s).Replace("%7B%7B", "{{").Replace("%7D%7D", "}}");

    private static List<string> Distinct(List<string> w) => w.Distinct().ToList();
}
