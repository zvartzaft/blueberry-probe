using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;

namespace ProbeDesk.Web.Services;

public sealed record HelpPage(string Slug, string Title, string Markdown, int Order);

/// <summary>Loads Markdown help pages embedded from docs/user/{lang}/ (works offline).</summary>
public sealed partial class HelpService
{
    public const string DefaultLanguage = "en";
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    private static readonly string[] Order =
        ["getting-started", "variables-environments", "cookie-csrf-login", "file-uploads", "import", "secrets", "shortcuts", "whats-new"];

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)] private static partial Regex H1();
    [GeneratedRegex(@"\]\((/help/[^)\s#]+)")] private static partial Regex InternalLink();

    public IReadOnlyList<HelpPage> Pages { get; }

    public HelpService(string language = DefaultLanguage)
    {
        var asm = typeof(HelpService).Assembly;
        var prefix = $"help/{language}/";
        var pages = new List<HelpPage>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            var logical = name.Replace('\\', '/');
            if (!logical.StartsWith(prefix, StringComparison.Ordinal) || !logical.EndsWith(".md")) continue;
            using var reader = new StreamReader(asm.GetManifestResourceStream(name)!);
            var md = reader.ReadToEnd();
            var slug = logical[prefix.Length..^3];
            var title = H1().Match(md) is { Success: true } m ? m.Groups[1].Value.Trim() : "";
            var idx = Array.IndexOf(Order, slug);
            pages.Add(new HelpPage(slug, title, md, idx < 0 ? 100 : idx));
        }
        Pages = pages.OrderBy(p => p.Order).ThenBy(p => p.Title).ToList();
    }

    public HelpPage? Find(string slug) => Pages.FirstOrDefault(p => p.Slug == slug);

    public static string Render(HelpPage page) => Markdown.ToHtml(page.Markdown, Pipeline);

    public IEnumerable<HelpPage> Search(string query) =>
        string.IsNullOrWhiteSpace(query) ? Pages
        : Pages.Where(p => p.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || p.Markdown.Contains(query, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> InternalSlugs(HelpPage page) =>
        InternalLink().Matches(page.Markdown).Select(m => m.Groups[1].Value["/help/".Length..]);
}
