using ProbeDesk.Core.Http;

namespace ProbeDesk.Core.Model;

public enum ExtractSource { Body, Header, Cookie }
public enum ExtractKind { Regex, JsonPath, Css, Name }
public enum AssertionKind { Status, Header, JsonPath }

/// <summary>Writes a value from a response into a variable.</summary>
public sealed class Extractor
{
    public ExtractSource Source { get; set; }
    public ExtractKind Kind { get; set; }
    public string Expression { get; set; } = "";
    public string TargetVariable { get; set; } = "";
}

public sealed class Assertion
{
    public AssertionKind Kind { get; set; }
    /// <summary>Header name or JSONPath. Unused for Status.</summary>
    public string Expression { get; set; } = "";
    public string Expected { get; set; } = "";
}

public sealed class SavedRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = "New request";
    public RequestSpec Spec { get; set; } = new();
    /// <summary>Ordered ids of saved requests to run first (login, CSRF fetch ...).</summary>
    public List<Guid> RunBefore { get; set; } = [];
    public List<Extractor> Extractors { get; set; } = [];
    public List<Assertion> Assertions { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>In-memory collection tree used by importers and exporters.</summary>
public sealed class FolderNode
{
    public string Name { get; set; } = "";
    public List<FolderNode> Folders { get; set; } = [];
    public List<SavedRequest> Requests { get; set; } = [];
}

public sealed class Variable
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsSecret { get; set; }
}

/// <summary>A pasted browser session cookie. Stored encrypted with the environment; values are never shown in the UI.</summary>
public sealed record StoredCookie(string Name, string Value);

public sealed class RequestEnvironment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool IsProduction { get; set; }
    public bool IgnoreSsl { get; set; }
    public string BaseUrl { get; set; } = "";
    public List<Variable> Variables { get; set; } = [];
    /// <summary>Optional allow-list. Empty means any host is allowed.</summary>
    public List<string> AllowedHosts { get; set; } = [];
    /// <summary>Auth and antiforgery cookies pasted from the browser (see <see cref="SessionCookies"/>).</summary>
    public List<StoredCookie> SessionCookies { get; set; } = [];
    /// <summary>Fetch a CSRF token and send it as X-CSRF-TOKEN on POST/PUT/PATCH/DELETE to the base URL's origin.</summary>
    public bool AutoCsrf { get; set; }
}

public sealed record HistoryEntry(Guid Id, DateTime TimeUtc, string Method, string Host, string Path, int Status, long DurationMs);

/// <summary>Protects secrets at rest (DPAPI on Windows).</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}
