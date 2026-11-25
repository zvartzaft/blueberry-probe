using ProbeDesk.Core.Model;

namespace ProbeDesk.Import;

public enum ReportSeverity { Info, Warning }

public sealed record ReportItem(ReportSeverity Severity, string Message);

/// <summary>Everything that could not be translated. Importers never silently drop content.</summary>
public sealed class ImportReport
{
    public List<ReportItem> Items { get; } = [];
    public void Warn(string message) { if (!Items.Any(i => i.Message == message)) Items.Add(new(ReportSeverity.Warning, message)); }
    public void Info(string message) { if (!Items.Any(i => i.Message == message)) Items.Add(new(ReportSeverity.Info, message)); }
    public int WarningCount => Items.Count(i => i.Severity == ReportSeverity.Warning);
}

public sealed class ImportResult
{
    public FolderNode Root { get; set; } = new();
    public List<RequestEnvironment> Environments { get; } = [];
    public ImportReport Report { get; } = new();
    /// <summary>True for single-file Postman v3 resources: the zip importer places them by their path.</summary>
    public bool PlaceByPath { get; set; }
    public int RequestCount => Count(Root);
    private static int Count(FolderNode n) => n.Requests.Count + n.Folders.Sum(Count);
}
