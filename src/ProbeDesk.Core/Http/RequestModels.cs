namespace ProbeDesk.Core.Http;

public enum BodyType { None, Raw, FormUrlEncoded, Multipart, Binary }

public enum AuthKind { None, Bearer, Basic, OAuth2ClientCredentials }

public sealed record HeaderEntry(string Name, string Value);

public sealed class FormField
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsFile { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "application/octet-stream";
}

public sealed class AuthSpec
{
    public AuthKind Kind { get; set; }
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string TokenUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "";
}

public sealed class RequestSpec
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public List<HeaderEntry> Headers { get; set; } = [];
    public List<HeaderEntry> QueryParams { get; set; } = [];
    public BodyType BodyType { get; set; } = BodyType.None;
    public string Body { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public List<FormField> FormFields { get; set; } = [];
    public string BinaryFilePath { get; set; } = "";
    public AuthSpec Auth { get; set; } = new();
    public bool FollowRedirects { get; set; }
    public int TimeoutSeconds { get; set; } = 30;

    public RequestSpec Clone() => new()
    {
        Method = Method, Url = Url, Headers = [.. Headers], QueryParams = [.. QueryParams], BodyType = BodyType,
        Body = Body, ContentType = ContentType, BinaryFilePath = BinaryFilePath,
        FormFields = FormFields.Select(f => new FormField
        {
            Name = f.Name, Value = f.Value, IsFile = f.IsFile, FilePath = f.FilePath, FileName = f.FileName, MimeType = f.MimeType
        }).ToList(),
        Auth = new AuthSpec
        {
            Kind = Auth.Kind, Token = Auth.Token, Username = Auth.Username, Password = Auth.Password,
            TokenUrl = Auth.TokenUrl, ClientId = Auth.ClientId, ClientSecret = Auth.ClientSecret, Scope = Auth.Scope
        },
        FollowRedirects = FollowRedirects, TimeoutSeconds = TimeoutSeconds
    };
}

public sealed class ResponseResult
{
    public int StatusCode { get; init; }
    public string ReasonPhrase { get; init; } = "";
    public TimeSpan Elapsed { get; init; }
    public TimeSpan TimeToFirstByte { get; init; }
    public long SizeBytes { get; init; }
    public List<HeaderEntry> Headers { get; init; } = [];
    public List<string> SetCookies { get; init; } = [];
    public string Body { get; init; } = "";
    public bool BodyTruncated { get; init; }
    /// <summary>Full body saved to disk when it exceeded the rendered limit.</summary>
    public string? SpoolPath { get; init; }
    public string FinalUrl { get; init; } = "";
    public int RedirectCount { get; init; }
}
