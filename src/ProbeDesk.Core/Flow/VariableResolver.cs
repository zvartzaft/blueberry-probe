using System.Text.RegularExpressions;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Core.Flow;

public static partial class VariableResolver
{
    [GeneratedRegex(@"\{\{\s*([^{}\s]+)\s*\}\}")]
    private static partial Regex Placeholder();

    public static string Resolve(string input, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Placeholder().Replace(input, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    /// <summary>Names used as {{name}} that are not defined.</summary>
    public static IEnumerable<string> Missing(RequestSpec spec, IReadOnlyDictionary<string, string> vars) =>
        Strings(spec).SelectMany(s => Placeholder().Matches(s).Select(m => m.Groups[1].Value))
            .Where(n => !vars.ContainsKey(n)).Distinct();

    public static RequestSpec Resolve(RequestSpec spec, IReadOnlyDictionary<string, string> vars, string baseUrl = "")
    {
        var r = spec.Clone();
        string R(string s) => Resolve(s, vars);
        r.Url = R(r.Url);
        if (baseUrl.Length > 0 && !Uri.TryCreate(r.Url, UriKind.Absolute, out _))
            r.Url = baseUrl.TrimEnd('/') + "/" + r.Url.TrimStart('/');
        r.Headers = r.Headers.Select(h => new HeaderEntry(R(h.Name), R(h.Value))).ToList();
        r.QueryParams = r.QueryParams.Select(h => new HeaderEntry(R(h.Name), R(h.Value))).ToList();
        r.Body = R(r.Body);
        r.ContentType = R(r.ContentType);
        foreach (var f in r.FormFields) { f.Name = R(f.Name); f.Value = R(f.Value); }
        var a = r.Auth;
        a.Token = R(a.Token); a.Username = R(a.Username); a.Password = R(a.Password);
        a.TokenUrl = R(a.TokenUrl); a.ClientId = R(a.ClientId); a.ClientSecret = R(a.ClientSecret); a.Scope = R(a.Scope);
        return r;
    }

    /// <summary>Variables for sending (real secrets).</summary>
    public static Dictionary<string, string> ToMap(RequestEnvironment? env) =>
        WithBaseUrl(env, env is null ? new() : env.Variables.Where(v => v.Name.Length > 0)
            .GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.Last().Value));

    /// <summary>Variables for preview (secrets masked).</summary>
    public static Dictionary<string, string> ToMaskedMap(RequestEnvironment? env) =>
        WithBaseUrl(env, env is null ? new() : env.Variables.Where(v => v.Name.Length > 0)
            .GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.Last().IsSecret ? "***" : g.Last().Value));

    // Imported collections use {{baseUrl}}; the environment's Base URL setting fills it when no such variable exists.
    private static Dictionary<string, string> WithBaseUrl(RequestEnvironment? env, Dictionary<string, string> map)
    {
        if (env is { BaseUrl.Length: > 0 } && !map.ContainsKey("baseUrl")) map["baseUrl"] = env.BaseUrl.TrimEnd('/');
        return map;
    }

    private static IEnumerable<string> Strings(RequestSpec s) =>
        new[] { s.Url, s.Body, s.ContentType, s.Auth.Token, s.Auth.Username, s.Auth.Password, s.Auth.TokenUrl, s.Auth.ClientId, s.Auth.ClientSecret, s.Auth.Scope }
            .Concat(s.Headers.SelectMany(h => new[] { h.Name, h.Value }))
            .Concat(s.QueryParams.SelectMany(h => new[] { h.Name, h.Value }))
            .Concat(s.FormFields.SelectMany(f => new[] { f.Name, f.Value }));
}
