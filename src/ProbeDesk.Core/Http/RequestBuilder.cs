using System.Net.Http.Headers;
using System.Text;

namespace ProbeDesk.Core.Http;

public static class RequestBuilder
{
    public static HttpRequestMessage Build(RequestSpec spec)
    {
        var uri = BuildUri(spec);
        if (string.IsNullOrWhiteSpace(spec.Method))
            throw new ArgumentException("Method is required.");

        var request = new HttpRequestMessage(new HttpMethod(spec.Method.Trim().ToUpperInvariant()), uri);
        HttpContent? content = BuildContent(spec);
        request.Content = content;

        foreach (var h in spec.Headers.Where(h => !string.IsNullOrWhiteSpace(h.Name)))
        {
            if (request.Headers.TryAddWithoutValidation(h.Name, h.Value)) continue;
            // Content headers (e.g. Content-Type) need content to attach to.
            request.Content = content ??= new ByteArrayContent([]);
            content.Headers.Remove(h.Name);
            content.Headers.TryAddWithoutValidation(h.Name, h.Value);
        }

        if (request.Headers.Authorization is null)
        {
            switch (spec.Auth.Kind)
            {
                case AuthKind.Bearer when spec.Auth.Token.Length > 0:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", spec.Auth.Token);
                    break;
                case AuthKind.Basic:
                    var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{spec.Auth.Username}:{spec.Auth.Password}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
                    break;
            }
        }
        return request;
    }

    public static Uri BuildUri(RequestSpec spec)
    {
        if (!Uri.TryCreate(spec.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("URL must be an absolute http(s) URL.");
        var extra = spec.QueryParams.Where(q => !string.IsNullOrWhiteSpace(q.Name)).ToList();
        if (extra.Count == 0) return uri;
        var ub = new UriBuilder(uri);
        var added = string.Join("&", extra.Select(q => $"{Uri.EscapeDataString(q.Name)}={Uri.EscapeDataString(q.Value)}"));
        ub.Query = string.IsNullOrEmpty(ub.Query) ? added : ub.Query.TrimStart('?') + "&" + added;
        return ub.Uri;
    }

    private static HttpContent? BuildContent(RequestSpec spec)
    {
        switch (spec.BodyType)
        {
            case BodyType.Raw when spec.Body.Length > 0:
                var sc = new StringContent(spec.Body, Encoding.UTF8);
                sc.Headers.Remove("Content-Type");
                sc.Headers.TryAddWithoutValidation("Content-Type", spec.ContentType);
                return sc;
            case BodyType.FormUrlEncoded:
                return new FormUrlEncodedContent(spec.FormFields.Where(f => !f.IsFile)
                    .Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
            case BodyType.Multipart:
                var mp = new MultipartFormDataContent();
                foreach (var f in spec.FormFields.Where(f => f.Name.Length > 0))
                {
                    if (!f.IsFile) { mp.Add(new StringContent(f.Value, Encoding.UTF8), f.Name); continue; }
                    var stream = OpenFile(f.FilePath);
                    var part = new StreamContent(stream);
                    part.Headers.ContentType = MediaTypeHeaderValue.TryParse(f.MimeType, out var mt)
                        ? mt : new MediaTypeHeaderValue("application/octet-stream");
                    mp.Add(part, f.Name, string.IsNullOrEmpty(f.FileName) ? Path.GetFileName(f.FilePath) : f.FileName);
                }
                return mp;
            case BodyType.Binary:
                var bin = new StreamContent(OpenFile(spec.BinaryFilePath));
                bin.Headers.TryAddWithoutValidation("Content-Type", spec.ContentType);
                return bin;
            default:
                return null;
        }
    }

    private static FileStream OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new ArgumentException("A file to upload is missing. Select the file again.");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
