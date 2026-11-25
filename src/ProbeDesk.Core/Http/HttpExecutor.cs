using System.Diagnostics;
using System.Text;

namespace ProbeDesk.Core.Http;

public sealed class HttpExecutor(HttpSession session, IReadOnlyList<string>? allowedHosts = null, string? spoolDirectory = null, string? uploadRoot = null)
{
    public const int MaxRedirects = 10;
    public const int MaxRenderedBytes = 2 * 1024 * 1024;
    public const long MaxSpoolBytes = 200L * 1024 * 1024;
    public const int MaxTimeoutSeconds = 600;

    public async Task<ResponseResult> SendAsync(RequestSpec spec, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(spec.TimeoutSeconds, 1, MaxTimeoutSeconds)));
        VerifyUploadPaths(spec);

        var sw = Stopwatch.StartNew();
        var current = spec;
        var redirects = 0;

        while (true)
        {
            using var request = RequestBuilder.Build(current);
            if (!HostPolicy.IsAllowed(allowedHosts, request.RequestUri!.Host))
                throw new BlockedHostException(request.RequestUri.Host);

            using var response = await session.Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var ttfb = sw.Elapsed;

            var status = (int)response.StatusCode;
            if (spec.FollowRedirects && status is 301 or 302 or 303 or 307 or 308
                && response.Headers.Location is { } location && redirects < MaxRedirects)
            {
                redirects++;
                var next = new Uri(request.RequestUri, location);
                current = NextHop(current, next, keepBody: status is 307 or 308,
                    sameOrigin: SameOrigin(next, request.RequestUri));
                continue;
            }

            var (body, size, truncated, spool) = await ReadBodyAsync(response, spoolDirectory, timeout.Token);
            sw.Stop();

            var headers = response.Headers.Concat(response.Content.Headers)
                .SelectMany(h => h.Value.Select(v => new HeaderEntry(h.Key, v))).ToList();
            var setCookies = response.Headers.TryGetValues("Set-Cookie", out var sc) ? sc.ToList() : [];

            return new ResponseResult
            {
                StatusCode = status,
                ReasonPhrase = response.ReasonPhrase ?? "",
                Elapsed = sw.Elapsed,
                TimeToFirstByte = ttfb,
                SizeBytes = size,
                Headers = headers,
                SetCookies = setCookies,
                Body = body,
                BodyTruncated = truncated,
                SpoolPath = spool,
                FinalUrl = request.RequestUri.ToString(),
                RedirectCount = redirects
            };
        }
    }

    private static RequestSpec NextHop(RequestSpec current, Uri next, bool keepBody, bool sameOrigin)
    {
        var n = current.Clone();
        n.Url = next.ToString();
        n.QueryParams = [];
        // A cross-origin 307/308 must not replay the body either.
        if (!keepBody || !sameOrigin)
        {
            if (!n.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)) n.Method = "GET";
            n.BodyType = BodyType.None; n.Body = ""; n.FormFields = []; n.BinaryFilePath = "";
        }
        if (!sameOrigin)
        {
            // Never forward credentials to a different origin (scheme, host or port).
            n.Auth = new AuthSpec();
            n.Headers = n.Headers.Where(h => !CredentialHeader(h.Name)).ToList();
        }
        return n;
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        a.Scheme == b.Scheme && a.Port == b.Port && a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase);

    private static bool CredentialHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("csrf", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("xsrf", StringComparison.OrdinalIgnoreCase);

    // Uploads may only come from the app upload folder; imported or hand-edited paths must not read arbitrary files.
    private void VerifyUploadPaths(RequestSpec spec)
    {
        if (uploadRoot is null) return;
        var root = Path.GetFullPath(uploadRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var paths = spec.FormFields.Where(f => f.IsFile).Select(f => f.FilePath).Append(spec.BinaryFilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p));
        foreach (var p in paths)
            if (!Path.GetFullPath(p).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A file to upload is outside the Blueberry Probe upload folder. Select the file again.");
    }

    private static FileStream StartSpool(string dir, MemoryStream kept, byte[] buffer, int offset, int read, out string path)
    {
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".bin");
        var spool = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        spool.Write(kept.GetBuffer(), 0, (int)kept.Length);
        spool.Write(buffer, offset, read - offset);
        return spool;
    }

    private static void DeleteQuietly(string? path)
    {
        try { if (path is not null) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static async Task<(string Body, long Size, bool Truncated, string? Spool)> ReadBodyAsync(
        HttpResponseMessage response, string? spoolDir, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[16 * 1024];
        using var kept = new MemoryStream();
        FileStream? spool = null;
        string? spoolPath = null;
        long total = 0;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (kept.Length < MaxRenderedBytes)
                {
                    var room = MaxRenderedBytes - (int)kept.Length;
                    kept.Write(buffer, 0, Math.Min(read, room));
                    if (read > room && spoolDir is not null) spool = StartSpool(spoolDir, kept, buffer, room, read, out spoolPath);
                }
                else if (spool is not null)
                {
                    if (total > MaxSpoolBytes) { spool.Dispose(); spool = null; DeleteQuietly(spoolPath); spoolPath = null; spoolDir = null; }
                    else spool.Write(buffer, 0, read);
                }
                else if (spoolDir is not null)
                {
                    // The preview filled up exactly at a chunk boundary: start the spool with this chunk.
                    spool = StartSpool(spoolDir, kept, buffer, 0, read, out spoolPath);
                }
            }
        }
        catch { spool?.Dispose(); spool = null; DeleteQuietly(spoolPath); throw; }
        finally { spool?.Dispose(); }

        var charset = response.Content.Headers.ContentType?.CharSet;
        Encoding enc;
        try { enc = charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset); }
        catch (ArgumentException) { enc = Encoding.UTF8; }
        return (enc.GetString(kept.ToArray()), total, total > MaxRenderedBytes, spoolPath);
    }
}
