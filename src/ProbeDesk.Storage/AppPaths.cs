using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProbeDesk.Storage;

public sealed record AppPaths(string DataDir)
{
    public string Database => Path.Combine(DataDir, "probedesk.db");
    public string Uploads => Path.Combine(DataDir, "uploads");
    public string Spool => Path.Combine(DataDir, "spool");

    /// <summary>%LOCALAPPDATA%/ProbeDesk on Windows, ~/.local/share/ProbeDesk on Linux.</summary>
    public static AppPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProbeDesk"));

    public void Ensure()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Uploads);
        Directory.CreateDirectory(Spool);
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
