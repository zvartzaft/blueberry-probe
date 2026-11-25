using System.Text.RegularExpressions;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Import;

/// <summary>
/// Parses .http / .rest files (VS Code REST Client, Visual Studio, common httpYac/JetBrains syntax).
/// Scripts and unsupported directives are reported and never executed.
/// </summary>
public static partial class HttpFileParser
{
    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
        { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT" };

    [GeneratedRegex(@"^@([A-Za-z_][\w.\-]*)\s*=\s*(.*)$")] private static partial Regex VarLine();
    [GeneratedRegex(@"^(?:#|//)\s*@(\S+)\s*(.*)$")] private static partial Regex Directive();
    [GeneratedRegex(@"^([A-Za-z]+)\s+(\S.*?)(?:\s+HTTP/\d(?:\.\d)?)?\s*$")] private static partial Regex RequestLine();
    [GeneratedRegex(@"^([!#$%&'*+\-.^_`|~\w]+)\s*:\s*(.*)$")] private static partial Regex HeaderLine();
    [GeneratedRegex(@"^\{\{[^}]*[;=(][^}]*\}\}\s*$")] private static partial Regex InlineScript();
    [GeneratedRegex(@"\{\{\s*(\$[\w.]+)")] private static partial Regex SystemVar();
    [GeneratedRegex(@"\{\{\s*\w+\.(?:response|request)\.")] private static partial Regex RequestRef();

    public static ImportResult Parse(string text, string fileName = "http-file")
    {
        var result = new ImportResult { Root = new FolderNode { Name = fileName } };
        var env = new RequestEnvironment { Name = fileName + " (variables)" };
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        var block = new List<string>();
        var title = "";
        void Flush()
        {
            if (block.Any(l => l.Trim().Length > 0)) ParseBlock(block, title, result, env);
            block.Clear();
        }
        foreach (var line in lines)
        {
            if (line.StartsWith("###"))
            {
                Flush();
                title = line.TrimStart('#').Trim();
            }
            else block.Add(line);
        }
        Flush();

        if (env.Variables.Count > 0) result.Environments.Add(env);
        foreach (var r in result.Root.Requests) ScanVariables(r.Spec, result.Report);
        return result;
    }

    private static void ParseBlock(List<string> block, string title, ImportResult result, RequestEnvironment env)
    {
        var report = result.Report;
        string? name = title.Length > 0 ? title : null;
        string? requestLine = null;
        var i = 0;

        for (; i < block.Count; i++)
        {
            var t = block[i].Trim();
            if (t.Length == 0) continue;
            if (VarLine().Match(t) is { Success: true } v)
            {
                env.Variables.Add(new Variable { Name = v.Groups[1].Value, Value = v.Groups[2].Value.Trim() });
                continue;
            }
            if (t.StartsWith("@import", StringComparison.Ordinal))
            {
                report.Warn("The httpYac directive '@import' is not supported; import the files separately.");
                continue;
            }
            if (Directive().Match(t) is { Success: true } d)
            {
                var key = d.Groups[1].Value;
                if (key == "name") name = d.Groups[2].Value.Trim();
                else if (key is "no-redirect" or "no-cookie-jar" or "note" or "prompt") report.Info($"The directive '@{key}' was ignored.");
                else report.Warn($"The directive '@{key}' is not supported and was ignored.");
                continue;
            }
            if (t.StartsWith('#') || t.StartsWith("//")) continue;
            if (t == "{{" || (t.StartsWith("{{") && !t.Contains("}}")))
            {
                report.Warn("A script block ({{ ... }}) was not imported and is never executed.");
                while (i < block.Count && !block[i].Contains("}}")) i++;
                continue;
            }
            if (InlineScript().IsMatch(t))
            {
                report.Warn("A script expression ({{ ... }}) was not imported and is never executed.");
                continue;
            }
            requestLine = t;
            i++;
            break;
        }
        if (requestLine is null) return;

        var spec = new RequestSpec();
        var rl = RequestLine().Match(requestLine);
        if (rl.Success && Verbs.Contains(rl.Groups[1].Value.ToUpperInvariant()) && rl.Groups[1].Value == rl.Groups[1].Value.ToUpperInvariant())
        {
            spec.Method = rl.Groups[1].Value;
            spec.Url = rl.Groups[2].Value;
        }
        else
        {
            spec.Url = Regex.Replace(requestLine, @"\s+HTTP/\d(?:\.\d)?\s*$", "");
        }

        // Query continuation lines (?a=b / &c=d)
        while (i < block.Count && block[i].TrimStart() is { } q && (q.StartsWith('?') || q.StartsWith('&')))
        { spec.Url += q.Trim(); i++; }

        // Headers until blank line
        while (i < block.Count)
        {
            var t = block[i].Trim();
            if (t.Length == 0) { i++; break; }
            if (t.StartsWith('#') || t.StartsWith("//")) { i++; continue; }
            var h = HeaderLine().Match(t);
            if (!h.Success) break;
            spec.Headers.Add(new HeaderEntry(h.Groups[1].Value, h.Groups[2].Value.Trim()));
            i++;
        }

        var bodyLines = new List<string>();
        for (; i < block.Count; i++)
        {
            var raw = block[i];
            var t = raw.Trim();
            if (t.StartsWith("> ") || t == ">")
            { report.Warn("Response handlers (> ...) were not imported."); continue; }
            if (t.StartsWith("<> ")) continue;
            if (t.StartsWith("??")) { report.Warn("httpYac conditions (?? ...) were not imported; create checks manually."); continue; }
            bodyLines.Add(raw);
        }
        while (bodyLines.Count > 0 && bodyLines[^1].Trim().Length == 0) bodyLines.RemoveAt(bodyLines.Count - 1);
        while (bodyLines.Count > 0 && bodyLines[0].Trim().Length == 0) bodyLines.RemoveAt(0);

        ApplyBody(spec, bodyLines, report, name ?? requestLine);

        result.Root.Requests.Add(new SavedRequest
        {
            Name = name ?? $"{spec.Method} {PathOf(spec.Url)}",
            Spec = spec
        });
    }

    private static void ApplyBody(RequestSpec spec, List<string> body, ImportReport report, string where)
    {
        var ct = spec.Headers.FirstOrDefault(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        if (body.Count == 0) return;

        var first = body[0].Trim();
        if (first.StartsWith("< ") && body.Count == 1)
        {
            spec.BodyType = BodyType.Binary;
            var refName = first[2..].Trim();
            spec.ContentType = ct.Length > 0 ? ct : "application/octet-stream";
            report.Warn($"The file reference '{refName}' in '{where}' was not imported; pick the file again.");
            return;
        }

        var joined = string.Join("\n", body);
        if (ct.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            spec.BodyType = BodyType.FormUrlEncoded;
            foreach (var pair in string.Concat(body.Select(l => l.Trim())).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                spec.FormFields.Add(new FormField
                {
                    Name = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]),
                    Value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '))
                });
            }
            return;
        }

        spec.BodyType = BodyType.Raw;
        spec.Body = joined;
        spec.ContentType = ct.Length > 0 ? ct : joined.TrimStart().StartsWith('{') || joined.TrimStart().StartsWith('[') ? "application/json" : "text/plain";
        if (body.Any(l => l.TrimStart().StartsWith("< ")))
            report.Warn($"A file include (< file) inside the body of '{where}' was not imported.");
    }

    private static void ScanVariables(RequestSpec spec, ImportReport report)
    {
        var all = string.Join("\n", new[] { spec.Url, spec.Body }.Concat(spec.Headers.Select(h => h.Value)));
        foreach (Match m in SystemVar().Matches(all))
            report.Warn($"The system variable {{{{{m.Groups[1].Value}}}}} is not supported; replace it with a fixed value.");
        if (RequestRef().IsMatch(all))
            report.Warn("Response references ({{name.response...}}) are not supported; use extractors and run-first steps instead.");
    }

    private static string PathOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url;
}
