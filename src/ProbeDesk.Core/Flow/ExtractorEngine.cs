using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Core.Flow;

public static class ExtractorEngine
{
    /// <summary>Returns the extracted value or null when nothing matched.</summary>
    public static string? Extract(Extractor ex, ResponseResult response, CookieContainer? cookies = null)
    {
        if (string.IsNullOrWhiteSpace(ex.Expression)) return null;
        switch (ex.Source)
        {
            case ExtractSource.Header:
                return response.Headers.FirstOrDefault(h => h.Name.Equals(ex.Expression, StringComparison.OrdinalIgnoreCase))?.Value;
            case ExtractSource.Cookie:
                foreach (var sc in response.SetCookies)
                {
                    var pair = sc.Split(';')[0];
                    var i = pair.IndexOf('=');
                    if (i > 0 && pair[..i].Trim().Equals(ex.Expression, StringComparison.Ordinal))
                        return WebUtility.UrlDecode(pair[(i + 1)..].Trim());
                }
                if (cookies is not null && Uri.TryCreate(response.FinalUrl, UriKind.Absolute, out var u))
                {
                    var c = cookies.GetCookies(u).Cast<Cookie>().FirstOrDefault(c => c.Name == ex.Expression);
                    return c is null ? null : WebUtility.UrlDecode(c.Value);
                }
                return null;
            default:
                return ex.Kind switch
                {
                    ExtractKind.Regex => Regex(response.Body, ex.Expression),
                    ExtractKind.JsonPath => JsonPathLite.Select(response.Body, ex.Expression),
                    ExtractKind.Css => Css(response.Body, ex.Expression),
                    _ => null
                };
        }
    }

    private static string? Regex(string body, string pattern)
    {
        try
        {
            var m = new Regex(pattern, RegexOptions.Singleline, TimeSpan.FromSeconds(2)).Match(body);
            if (!m.Success) return null;
            return m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
        }
        catch (ArgumentException) { return null; }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>Expression "selector@attribute" returns the attribute; without "@" returns the text.</summary>
    private static string? Css(string html, string expression)
    {
        var at = expression.LastIndexOf('@');
        var selector = at >= 0 ? expression[..at] : expression;
        var attr = at >= 0 ? expression[(at + 1)..] : null;
        try
        {
            var el = new HtmlParser().ParseDocument(html).QuerySelector(selector);
            if (el is null) return null;
            return attr is null ? el.TextContent.Trim() : el.GetAttribute(attr);
        }
        catch (Exception) { return null; }
    }
}
