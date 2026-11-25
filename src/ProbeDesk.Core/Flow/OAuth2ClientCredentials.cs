using System.Net;
using System.Text.Json;
using ProbeDesk.Core.Http;

namespace ProbeDesk.Core.Flow;

public static class OAuth2ClientCredentials
{
    /// <summary>Requests an access token. Errors are generic so secrets never leak into messages.</summary>
    public static async Task<(string Token, int? ExpiresInSeconds)> FetchAsync(
        HttpExecutor executor, AuthSpec auth, CancellationToken ct = default)
    {
        var spec = new RequestSpec
        {
            Method = "POST", Url = auth.TokenUrl, BodyType = BodyType.FormUrlEncoded,
            FormFields =
            [
                new() { Name = "grant_type", Value = "client_credentials" },
                new() { Name = "client_id", Value = auth.ClientId },
                new() { Name = "client_secret", Value = auth.ClientSecret },
                .. auth.Scope.Length > 0 ? [new FormField { Name = "scope", Value = auth.Scope }] : Array.Empty<FormField>()
            ]
        };
        var res = await executor.SendAsync(spec, ct);
        if (res.StatusCode != (int)HttpStatusCode.OK)
            throw new InvalidOperationException($"Token request failed with status {res.StatusCode}.");
        var token = JsonPathLite.Select(res.Body, "$.access_token");
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Token response did not contain access_token.");
        int? exp = int.TryParse(JsonPathLite.Select(res.Body, "$.expires_in"), out var e) ? e : null;
        return (token, exp);
    }
}
