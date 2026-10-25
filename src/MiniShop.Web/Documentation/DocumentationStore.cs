using System.Text.RegularExpressions;

namespace MiniShop.Web.Documentation;

/// <summary>
/// Příručka (dokumentace v aplikaci): načítá stránky z Markdown souborů ve složce <c>docs/prirucka</c>.
/// Adresa stránky (slug) = cesta k souboru bez <c>.md</c>; <c>index.md</c> je stránka složky
/// (<c>eda/index.md</c> → <c>eda</c>, kořenový <c>index.md</c> → prázdný slug).
/// </summary>
/// <remarks>
/// Soubory se čtou při každém požadavku (je jich pár desítek a jsou malé) – při vývoji se tak změna textu
/// projeví hned po obnovení stránky. Slug přichází z URL, proto smí obsahovat jen malá písmena, číslice,
/// pomlčky a lomítka: „..“ ani absolutní cesta neprojde a mimo složku příručky se nic nepřečte.
/// </remarks>
public sealed partial class DocumentationStore(string rootPath)
{
    private const string IndexFile = "index.md";

    public string RootPath { get; } = Path.GetFullPath(rootPath);

    /// <summary>Stránka podle slugu, nebo null (neexistuje / nepovolený tvar adresy).</summary>
    public DocumentationPage? Find(string? slug)
    {
        slug = (slug ?? string.Empty).Trim('/');
        if (slug.Length > 0 && !SlugPattern().IsMatch(slug))
        {
            return null;
        }

        string[] candidates = slug.Length == 0
            ? [IndexFile]
            : [slug + ".md", Path.Combine(slug, IndexFile)];

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(Path.Combine(RootPath, candidate));
            if (IsInsideRoot(fullPath) && File.Exists(fullPath))
            {
                return Load(fullPath);
            }
        }

        return null;
    }

    /// <summary>Všechny stránky příručky.</summary>
    public IReadOnlyList<DocumentationPage> All() =>
        Directory.Exists(RootPath)
            ? Directory.EnumerateFiles(RootPath, "*.md", SearchOption.AllDirectories).Select(Load).ToList()
            : [];

    /// <summary>Strom stránek pro navigaci: kořen = úvodní stránka, děti seřazené podle <c>order</c> a názvu.</summary>
    public DocumentationNode? Tree()
    {
        var pages = All().ToDictionary(p => p.Slug);
        if (!pages.TryGetValue(string.Empty, out var root))
        {
            return null;
        }

        var childrenBySlug = pages.Values
            .Where(p => p.Slug.Length > 0)
            .GroupBy(p => ParentSlug(p.Slug))
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Order).ThenBy(p => p.Title).ToList());

        DocumentationNode Build(DocumentationPage page) => new(page,
            childrenBySlug.TryGetValue(page.Slug, out var children) ? children.Select(Build).ToList() : []);

        return Build(root);
    }

    /// <summary>Nadřazené stránky od úvodu po rodiče (drobečková navigace). Chybějící stránku složky přeskočí.</summary>
    public IReadOnlyList<DocumentationPage> Ancestors(string slug)
    {
        var result = new List<DocumentationPage>();
        while (slug.Length > 0)
        {
            slug = ParentSlug(slug);
            if (Find(slug) is { } parent)
            {
                result.Insert(0, parent);
            }
        }

        return result;
    }

    /// <summary>"eda/outbox" → "eda", "eda" → "" (úvod).</summary>
    public static string ParentSlug(string slug)
    {
        var lastSlash = slug.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : slug[..lastSlash];
    }

    private DocumentationPage Load(string fullPath)
    {
        var relative = Path.GetRelativePath(RootPath, fullPath).Replace('\\', '/');
        var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
        var fileName = Path.GetFileName(relative);
        var slug = fileName == IndexFile
            ? directory
            : (directory.Length == 0 ? string.Empty : directory + "/") + Path.GetFileNameWithoutExtension(fileName);

        var (frontMatter, markdown) = SplitFrontMatter(File.ReadAllText(fullPath));
        return new DocumentationPage(
            slug,
            directory,
            frontMatter.GetValueOrDefault("title") ?? slug,
            frontMatter.GetValueOrDefault("summary"),
            int.TryParse(frontMatter.GetValueOrDefault("order"), out var order) ? order : int.MaxValue,
            markdown);
    }

    /// <summary>
    /// Hlavička stránky mezi řádky <c>---</c> na začátku souboru, jednoduché <c>klíč: hodnota</c>
    /// (title, summary, order). Celý YAML parser kvůli třem polím nepotřebujeme.
    /// </summary>
    private static (Dictionary<string, string> FrontMatter, string Markdown) SplitFrontMatter(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return (values, text);
        }

        var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (end < 0)
        {
            return (values, text);
        }

        foreach (var line in lines[1..end])
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                values[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        return (values, string.Join('\n', lines[(end + 1)..]));
    }

    private bool IsInsideRoot(string fullPath) =>
        fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z0-9-]+(/[a-z0-9-]+)*$")]
    private static partial Regex SlugPattern();
}

/// <param name="Slug">Adresa stránky v příručce („eda/outbox“, úvod = prázdný řetězec).</param>
/// <param name="Directory">Složka souboru v příručce – od ní se počítají relativní odkazy na jiné stránky.</param>
public sealed record DocumentationPage(string Slug, string Directory, string Title, string? Summary, int Order, string Markdown);

public sealed record DocumentationNode(DocumentationPage Page, IReadOnlyList<DocumentationNode> Children);
