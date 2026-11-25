using ProbeDesk.Core.Flow;
using ProbeDesk.Core.Http;

namespace ProbeDesk.Web.Services;

/// <summary>Maps exceptions to generic messages. Raw exception text never reaches the UI.</summary>
public static class ErrorText
{
    public static string For(Exception ex) => ex switch
    {
        MissingVariableException m => "Missing variables: " + string.Join(", ", m.Names),
        BlockedHostException b => $"The host '{b.Host}' is not allowed in this environment.",
        FlowException f => f.Message.Contains("cycle") ? "The run-first steps contain a cycle."
            : f.Message.Contains("deeply") ? "The run-first steps are nested too deeply."
            : "A step in the flow no longer exists.",
        ArgumentException => "The request is invalid. Check the URL, headers and body.",
        UriFormatException => "The URL is invalid.",
        FileNotFoundException => "A file used by the request was not found. Pick the file again.",
        DirectoryNotFoundException => "A file used by the request was not found. Pick the file again.",
        HttpRequestException => "The connection failed. Check the address and your network.",
        OperationCanceledException => "The request was cancelled or timed out.",
        _ => "An unexpected error occurred."
    };
}
