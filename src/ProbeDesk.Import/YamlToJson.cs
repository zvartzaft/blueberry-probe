using System.Globalization;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ProbeDesk.Import;

/// <summary>Converts YAML to JSON so YAML files can reuse the JSON importers. Aliases are rejected (expansion attacks).</summary>
public static class YamlToJson
{
    private const int MaxDepth = 64;

    /// <exception cref="FormatException">The text is not valid or supported YAML.</exception>
    public static string Convert(string yaml)
    {
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlException ex) { throw new FormatException("The file is not valid YAML: " + ex.Message, ex); }
        if (stream.Documents.Count == 0) throw new FormatException("The YAML file is empty.");

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) Write(w, stream.Documents[0].RootNode, 0, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance));
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Write(Utf8JsonWriter w, YamlNode node, int depth, HashSet<YamlNode> visited)
    {
        if (depth > MaxDepth) throw new FormatException("The YAML structure is nested too deeply.");
        if (!visited.Add(node)) throw new FormatException("YAML aliases and anchors are not supported.");
        switch (node)
        {
            case YamlMappingNode map:
                w.WriteStartObject();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (k, v) in map.Children)
                {
                    if (k is not YamlScalarNode ks) throw new FormatException("Only plain text keys are supported in YAML.");
                    var key = ks.Value ?? "";
                    if (seen.Add(key)) { w.WritePropertyName(key); Write(w, v, depth + 1, visited); }
                }
                w.WriteEndObject();
                break;
            case YamlSequenceNode seq:
                w.WriteStartArray();
                foreach (var c in seq.Children) Write(w, c, depth + 1, visited);
                w.WriteEndArray();
                break;
            case YamlScalarNode s:
                WriteScalar(w, s);
                break;
            default:
                throw new FormatException("YAML aliases and anchors are not supported.");
        }
    }

    private static void WriteScalar(Utf8JsonWriter w, YamlScalarNode s)
    {
        var v = s.Value ?? "";
        if (s.Style != ScalarStyle.Plain) { w.WriteStringValue(v); return; }
        if (v is "" or "~" or "null" or "Null" or "NULL") w.WriteNullValue();
        else if (v is "true" or "True" or "TRUE") w.WriteBooleanValue(true);
        else if (v is "false" or "False" or "FALSE") w.WriteBooleanValue(false);
        else if (long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) && (v.Length == 1 || !v.TrimStart('-', '+').StartsWith('0'))) w.WriteNumberValue(l);
        else w.WriteStringValue(v);
    }
}
