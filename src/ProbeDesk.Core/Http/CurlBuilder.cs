using System.Text;

namespace ProbeDesk.Core.Http;

public static class CurlBuilder
{
    private static readonly HashSet<string> Sensitive =
        new(StringComparer.OrdinalIgnoreCase) { "Authorization", "Cookie", "Proxy-Authorization", "X-Api-Key" };

    public const string Mask = "***";

    /// <summary>Builds a cURL command. Sensitive values are masked unless includeSecrets is true.</summary>
    public static string Build(RequestSpec spec, bool includeSecrets = false)
    {
        var url = spec.Url;
        try { url = RequestBuilder.BuildUri(spec).ToString(); } catch (ArgumentException) { }

        var sb = new StringBuilder("curl -X ").Append(Quote(spec.Method.ToUpperInvariant()));
        sb.Append(' ').Append(Quote(url));
        if (spec.FollowRedirects) sb.Append(" -L");

        foreach (var h in spec.Headers.Where(h => !string.IsNullOrWhiteSpace(h.Name)))
        {
            var value = !includeSecrets && Sensitive.Contains(h.Name) ? Mask : h.Value;
            sb.Append(" -H ").Append(Quote($"{h.Name}: {value}"));
        }

        var a = spec.Auth;
        if (a.Kind == AuthKind.Bearer && a.Token.Length > 0)
            sb.Append(" -H ").Append(Quote($"Authorization: Bearer {(includeSecrets ? a.Token : Mask)}"));
        else if (a.Kind == AuthKind.Basic)
            sb.Append(" -u ").Append(Quote($"{a.Username}:{(includeSecrets ? a.Password : Mask)}"));

        bool HasCt() => spec.Headers.Any(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        switch (spec.BodyType)
        {
            case BodyType.Raw when spec.Body.Length > 0:
                if (!HasCt()) sb.Append(" -H ").Append(Quote($"Content-Type: {spec.ContentType}"));
                sb.Append(" --data-raw ").Append(Quote(spec.Body));
                break;
            case BodyType.FormUrlEncoded:
                foreach (var f in spec.FormFields.Where(f => !f.IsFile))
                    sb.Append(" --data-urlencode ").Append(Quote($"{f.Name}={f.Value}"));
                break;
            case BodyType.Multipart:
                foreach (var f in spec.FormFields)
                    sb.Append(" -F ").Append(Quote(f.IsFile ? $"{f.Name}=@{f.FilePath}" : $"{f.Name}={f.Value}"));
                break;
            case BodyType.Binary:
                if (!HasCt()) sb.Append(" -H ").Append(Quote($"Content-Type: {spec.ContentType}"));
                sb.Append(" --data-binary ").Append(Quote("@" + spec.BinaryFilePath));
                break;
        }
        return sb.ToString();
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
