using System.Net;

namespace ProbeDesk.Core.Http;

/// <summary>One session per user and environment. The jar is in memory; stored session cookies are seeded into it (see <see cref="SessionCookies"/>).</summary>
public sealed class HttpSession : IDisposable
{
    public CookieContainer Cookies { get; } = new();
    public HttpClient Client { get; }
    /// <summary>Set once the environment's stored session cookies have been loaded into <see cref="Cookies"/>.</summary>
    public bool CookiesSeeded { get; set; }
    /// <summary>Values written by extractors during this session. Memory only.</summary>
    public Dictionary<string, string> Variables { get; } = new();
    /// <summary>Cached OAuth2 tokens, keyed by token URL + client id. Memory only.</summary>
    public Dictionary<string, (string Token, DateTimeOffset Expires)> OAuthTokens { get; } = new();

    public HttpSession(bool ignoreSsl = false)
    {
        var handler = new SocketsHttpHandler
        {
            CookieContainer = Cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        if (ignoreSsl)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        Client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => Client.Dispose();
}
