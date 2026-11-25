using System.IO.Compression;
using System.Text;

namespace ProbeDesk.Import;

/// <summary>Imports one file by name or content: Postman JSON/YAML, .http/.rest. Unknown files are reported.</summary>
public static class FileImporter
{
    public static ImportResult Import(string fileName, string text)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var kind = ext switch
        {
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            ".http" or ".rest" => "http",
            _ => Sniff(text)
        };
        switch (kind)
        {
            case "json": return PostmanImporter.Import(text, fileName);
            case "yaml": return PostmanImporter.ImportYaml(text, fileName);
            case "http": return HttpFileParser.Parse(text, stem.Length > 0 ? stem : "http-file");
            default:
                var r = new ImportResult();
                r.Report.Warn("The file type is not supported and was skipped.");
                return r;
        }
    }

    /// <summary>For pasted Postman content: JSON first, then YAML.</summary>
    public static ImportResult ImportPostmanText(string text, string fileName = "")
    {
        var r = text.TrimStart().StartsWith('{') ? PostmanImporter.Import(text, fileName) : PostmanImporter.ImportYaml(text, fileName);
        if (r.PlaceByPath && r.RequestCount == 0)
            r.Report.Info("This is a collection definition without requests. Requests are separate files: zip the exported folder and import the zip.");
        return r;
    }

    private static string Sniff(string text)
    {
        var t = text.TrimStart();
        if (t.StartsWith('{')) return "json";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(?:###|(?:GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+\S)", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))) return "http";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(?:item|values|info|\$kind)\s*:", System.Text.RegularExpressions.RegexOptions.Multiline, TimeSpan.FromSeconds(1))) return "yaml";
        return "unknown";
    }
}

/// <summary>Reads a zip in memory (never extracts to disk) with strict limits, and merges every supported entry into one result.</summary>
public static class ZipImporter
{
    public const int MaxEntries = 5000;
    public const long MaxEntryBytes = 5 * 1024 * 1024;
    public const long MaxTotalBytes = 25 * 1024 * 1024;

    public static ImportResult Import(Stream zip, string zipName = "Zip import")
    {
        var result = new ImportResult { Root = new Core.Model.FolderNode { Name = zipName } };
        ZipArchive archive;
        try { archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { result.Report.Warn("The file is not a valid zip archive."); return result; }

        using (archive)
        {
            var entries = archive.Entries.Where(e => e.Name.Length > 0).ToList();
            if (entries.Count > MaxEntries)
            {
                result.Report.Warn($"The archive has {entries.Count} files; the limit is {MaxEntries}. Nothing was imported.");
                return result;
            }

            long total = 0;
            var examples = 0;
            foreach (var e in entries)
            {
                var path = e.FullName.Replace('\\', '/');
                if (path.StartsWith("__MACOSX/", StringComparison.Ordinal) || e.Name.StartsWith('.')) continue;
                if (path.Split('/').Any(p => p == "..") || Path.IsPathRooted(path))
                {
                    result.Report.Warn($"{path}: unsafe path, skipped.");
                    continue;
                }
                if (e.Name.EndsWith(".example.yaml", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".example.yml", StringComparison.OrdinalIgnoreCase)) { examples++; continue; }
                if (e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { result.Report.Warn($"{path}: nested zip archives are not opened."); continue; }

                var text = ReadLimited(e, ref total, out var problem);
                if (problem is not null) { result.Report.Warn($"{path}: {problem}"); if (total > MaxTotalBytes) break; continue; }

                ImportResult sub;
                try { sub = FileImporter.Import(e.Name, text!); }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or System.Text.Json.JsonException)
                {
                    result.Report.Warn($"{path}: the content could not be read.");
                    continue;
                }
                Merge(result, sub, path);
            }
            if (examples > 0) result.Report.Info($"{examples} saved response example file(s) (*.example.yaml) were skipped: they are example responses, not requests.");
        }
        if (result.RequestCount == 0 && result.Environments.Count == 0 && result.Report.WarningCount == 0)
            result.Report.Warn("The archive contains no supported files (.json, .yaml, .yml, .http, .rest).");
        return result;
    }

    private static string? ReadLimited(ZipArchiveEntry e, ref long total, out string? problem)
    {
        problem = null;
        if (e.Length > MaxEntryBytes) { problem = "file is larger than 5 MB, skipped."; return null; }
        using var s = e.Open();
        var buf = new byte[81920];
        using var ms = new MemoryStream();
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            total += n;
            if (ms.Length + n > MaxEntryBytes) { problem = "file is larger than 5 MB, skipped."; return null; }
            if (total > MaxTotalBytes) { problem = "the archive is larger than 25 MB when unpacked. Import stopped."; return null; }
            ms.Write(buf, 0, n);
        }
        return new UTF8Encoding(false).GetString(ms.ToArray());
    }

    private static void Merge(ImportResult into, ImportResult sub, string path)
    {
        foreach (var i in sub.Report.Items)
        {
            var msg = $"{path}: {i.Message}";
            if (i.Severity == ReportSeverity.Warning) into.Report.Warn(msg); else into.Report.Info(msg);
        }
        if (sub.PlaceByPath)
        {
            var dirs = path.Split('/')[..^1].Where(d => d != ".resources").ToList();
            var node = into.Root;
            foreach (var d in dirs)
            {
                var next = node.Folders.FirstOrDefault(f => f.Name == d);
                if (next is null) { next = new Core.Model.FolderNode { Name = d }; node.Folders.Add(next); }
                node = next;
            }
            node.Requests.AddRange(sub.Root.Requests);
            foreach (var env in sub.Environments)
            {
                if (env.Name == "definition (variables)") env.Name = (dirs.Count > 0 ? dirs[^1] : into.Root.Name) + " (variables)";
                into.Environments.Add(env);
            }
            return;
        }
        if (sub.RequestCount > 0) into.Root.Folders.Add(sub.Root);
        into.Environments.AddRange(sub.Environments);
    }
}
