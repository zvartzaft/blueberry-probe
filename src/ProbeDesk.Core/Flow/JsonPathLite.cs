using System.Text.Json;

namespace ProbeDesk.Core.Flow;

/// <summary>Minimal JSONPath: $.a.b, $['a'], $.items[0].id.</summary>
public static class JsonPathLite
{
    public static string? Select(string json, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { return null; }
        using (doc)
        {
            var el = doc.RootElement;
            var i = 0;
            var p = path.Trim();
            if (p.StartsWith('$')) i = 1;
            while (i < p.Length)
            {
                if (p[i] == '.')
                {
                    i++;
                    var start = i;
                    while (i < p.Length && p[i] != '.' && p[i] != '[') i++;
                    if (!TryProp(ref el, p[start..i])) return null;
                }
                else if (p[i] == '[')
                {
                    var end = p.IndexOf(']', i);
                    if (end < 0) return null;
                    var inner = p[(i + 1)..end].Trim();
                    i = end + 1;
                    if (inner.Length >= 2 && (inner[0] is '\'' or '"') && inner[^1] == inner[0])
                    {
                        if (!TryProp(ref el, inner[1..^1])) return null;
                    }
                    else if (int.TryParse(inner, out var idx) && el.ValueKind == JsonValueKind.Array && idx >= 0 && idx < el.GetArrayLength())
                        el = el[idx];
                    else return null;
                }
                else return null;
            }
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Null => "null",
                _ => el.GetRawText()
            };
        }
    }

    private static bool TryProp(ref JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var next)) return false;
        el = next;
        return true;
    }
}
