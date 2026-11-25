using System.Text;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Import;

/// <summary>Parses a pasted cURL command into a request. Unknown options are reported.</summary>
public static class CurlParser
{
    public static (SavedRequest? Request, ImportReport Report) Parse(string command)
    {
        var report = new ImportReport();
        var tokens = Tokenize(command);
        if (tokens.Count == 0 || !tokens[0].Equals("curl", StringComparison.OrdinalIgnoreCase))
        {
            report.Warn("The command must start with 'curl'.");
            return (null, report);
        }

        var spec = new RequestSpec();
        string? method = null;
        var data = new List<string>();
        var urlEncoded = new List<FormField>();
        var form = new List<FormField>();

        string Next(ref int i) => i + 1 < tokens.Count ? tokens[++i] : "";

        for (var i = 1; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t)
            {
                case "-X" or "--request": method = Next(ref i).ToUpperInvariant(); break;
                case "-H" or "--header":
                    var h = Next(ref i); var c = h.IndexOf(':');
                    if (c > 0) spec.Headers.Add(new HeaderEntry(h[..c].Trim(), h[(c + 1)..].Trim()));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii":
                    data.Add(Next(ref i)); break;
                case "--data-urlencode":
                    var du = Next(ref i); var e = du.IndexOf('=');
                    urlEncoded.Add(new FormField { Name = e < 0 ? "" : du[..e], Value = e < 0 ? du : du[(e + 1)..] });
                    break;
                case "-F" or "--form" or "--form-string":
                    var f = Next(ref i); var eq = f.IndexOf('=');
                    if (eq > 0)
                    {
                        var v = f[(eq + 1)..];
                        form.Add(v.StartsWith('@')
                            ? new FormField { Name = f[..eq], IsFile = true, FileName = Path.GetFileName(v[1..].Split(';')[0]) }
                            : new FormField { Name = f[..eq], Value = v });
                    }
                    break;
                case "-u" or "--user":
                    var up = Next(ref i); var colon = up.IndexOf(':');
                    spec.Auth = new AuthSpec { Kind = AuthKind.Basic, Username = colon < 0 ? up : up[..colon], Password = colon < 0 ? "" : up[(colon + 1)..] };
                    break;
                case "-L" or "--location": spec.FollowRedirects = true; break;
                case "-b" or "--cookie": spec.Headers.Add(new HeaderEntry("Cookie", Next(ref i))); break;
                case "-A" or "--user-agent": spec.Headers.Add(new HeaderEntry("User-Agent", Next(ref i))); break;
                case "--url": spec.Url = Next(ref i); break;
                case "-I" or "--head": method = "HEAD"; break;
                case "-k" or "--insecure":
                    report.Warn("The -k/--insecure flag was not imported; enable 'ignore certificate errors' on the environment if needed.");
                    break;
                case "-s" or "--silent" or "-S" or "-v" or "--verbose" or "-i" or "--include" or "--compressed" or "-g" or "--globoff":
                    break;
                default:
                    if (t.StartsWith('-')) report.Warn($"The flag '{t}' is not supported and was ignored.");
                    else if (spec.Url.Length == 0) spec.Url = t;
                    break;
            }
        }

        if (spec.Url.Length == 0) { report.Warn("No URL was found in the command."); return (null, report); }

        var hasCt = spec.Headers.FirstOrDefault(x => x.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
        if (form.Count > 0) { spec.BodyType = BodyType.Multipart; spec.FormFields = form; }
        else if (urlEncoded.Count > 0) { spec.BodyType = BodyType.FormUrlEncoded; spec.FormFields = urlEncoded; }
        else if (data.Count > 0)
        {
            spec.BodyType = BodyType.Raw;
            spec.Body = string.Join("&", data);
            spec.ContentType = hasCt ?? "application/x-www-form-urlencoded";
        }
        spec.Method = method ?? (spec.BodyType == BodyType.None ? "GET" : "POST");

        var name = $"{spec.Method} {(Uri.TryCreate(spec.Url, UriKind.Absolute, out var u) ? u.AbsolutePath : spec.Url)}";
        return (new SavedRequest { Name = name, Spec = spec }, report);
    }

    /// <summary>Shell-like tokenizer: quotes, backslash escapes, and line continuations (\ and ^).</summary>
    public static List<string> Tokenize(string s)
    {
        s = s.Replace("\\\r\n", " ").Replace("\\\n", " ").Replace("^\r\n", " ").Replace("^\n", " ");
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inToken = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\'')
            {
                inToken = true;
                for (i++; i < s.Length && s[i] != '\''; i++) sb.Append(s[i]);
            }
            else if (c == '"')
            {
                inToken = true;
                for (i++; i < s.Length && s[i] != '"'; i++)
                {
                    if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] is '"' or '\\' or '$' or '`') i++;
                    sb.Append(s[i]);
                }
            }
            else if (c == '\\' && i + 1 < s.Length) { inToken = true; sb.Append(s[++i]); }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken) { tokens.Add(sb.ToString()); sb.Clear(); inToken = false; }
            }
            else { inToken = true; sb.Append(c); }
        }
        if (inToken) tokens.Add(sb.ToString());
        return tokens;
    }
}
