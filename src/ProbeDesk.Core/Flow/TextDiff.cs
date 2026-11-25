namespace ProbeDesk.Core.Flow;

public enum DiffKind { Same, Added, Removed }

public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>Line-based diff (LCS) used to compare two responses.</summary>
public static class TextDiff
{
    public static List<DiffLine> Diff(string before, string after)
    {
        var a = before.ReplaceLineEndings("\n").Split('\n');
        var b = after.ReplaceLineEndings("\n").Split('\n');
        if ((long)a.Length * b.Length > 4_000_000)
            return [.. a.Select(l => new DiffLine(DiffKind.Removed, l)), .. b.Select(l => new DiffLine(DiffKind.Added, l))];

        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { result.Add(new(DiffKind.Same, a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) result.Add(new(DiffKind.Removed, a[x++]));
            else result.Add(new(DiffKind.Added, b[y++]));
        }
        while (x < a.Length) result.Add(new(DiffKind.Removed, a[x++]));
        while (y < b.Length) result.Add(new(DiffKind.Added, b[y++]));
        return result;
    }
}
