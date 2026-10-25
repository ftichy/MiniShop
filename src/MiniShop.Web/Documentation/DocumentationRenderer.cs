using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MiniShop.Web.Documentation;

/// <summary>
/// Převod stránky příručky z Markdownu na HTML (knihovna Markdig).
/// </summary>
/// <remarks>
/// Kromě běžného Markdownu umí:
/// <list type="bullet">
/// <item>bloky <c>```mermaid</c> → <c>&lt;div class="mermaid"&gt;</c>, diagram vykreslí v prohlížeči Mermaid,</item>
/// <item>odkaz na jinou stránku příručky jako na soubor (<c>[Inbox](inbox.md)</c>) → <c>/Docs/eda/inbox</c>,</item>
/// <item>odkaz do zdrojového kódu <c>[OrderService](code:src/…/OrderService.cs)</c> → GitHub (commit, který běží),</item>
/// <item>automatické bloky <c>{{auto:contracts}}</c> – obsah vygenerovaný z kódu, viz <see cref="DocumentationAutoBlocks"/>.</item>
/// </list>
/// </remarks>
public sealed partial class DocumentationRenderer(DocumentationOptions options)
{
    public const string BaseUrl = "/Docs";
    private const string CodeScheme = "code:";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()   // tabulky, Mermaid (diagrams), kotvy u nadpisů, atributy {.class}…
        .Build();

    public RenderedDocumentationPage Render(DocumentationPage page)
    {
        var markdown = AutoBlockPattern().Replace(page.Markdown, match => DocumentationAutoBlocks.Render(match.Groups[1].Value));
        var document = Markdown.Parse(markdown, Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            RewriteLink(link, page);
        }

        var headings = document.Descendants<HeadingBlock>()
            .Where(h => h.Level == 2 && h.GetAttributes().Id is not null)
            .Select(h => new DocumentationHeading(h.GetAttributes().Id!, InlineText(h.Inline)))
            .ToList();

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return new RenderedDocumentationPage(writer.ToString(), headings);
    }

    /// <summary>Adresa stránky příručky ("" → /Docs, "eda/outbox" → /Docs/eda/outbox).</summary>
    public static string PageUrl(string slug) => slug.Length == 0 ? BaseUrl : $"{BaseUrl}/{slug}";

    /// <summary>
    /// Odkaz do kódu na GitHubu. Na produkci vede na commit, ze kterého je image (proměnná Documentation__Commit
    /// = IMAGE_TAG „sha-…“), jinak na větev main.
    /// </summary>
    public string CodeUrl(string path)
    {
        path = path.TrimStart('/');
        var kind = path.EndsWith('/') ? "tree" : "blob";
        return $"{options.RepositoryUrl.TrimEnd('/')}/{kind}/{CommitOrBranch()}/{path.TrimEnd('/')}";
    }

    private string CommitOrBranch()
    {
        var match = CommitPattern().Match(options.Commit ?? string.Empty);
        return match.Success ? match.Groups[1].Value : "main";
    }

    private void RewriteLink(LinkInline link, DocumentationPage page)
    {
        var url = link.Url;
        if (string.IsNullOrEmpty(url) || link.IsImage)
        {
            return;
        }

        if (url.StartsWith(CodeScheme, StringComparison.Ordinal))
        {
            link.Url = CodeUrl(url[CodeScheme.Length..]);
            MarkExternal(link, "doc-code-link");
        }
        else if (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal))
        {
            MarkExternal(link, "doc-external-link");
        }
        else if (ToPageUrl(url, page) is { } pageUrl)
        {
            link.Url = pageUrl;
        }
    }

    /// <summary>"inbox.md#idempotence" na stránce ve složce "eda" → "/Docs/eda/inbox#idempotence".</summary>
    private static string? ToPageUrl(string url, DocumentationPage page)
    {
        var hashIndex = url.IndexOf('#');
        var path = hashIndex < 0 ? url : url[..hashIndex];
        var fragment = hashIndex < 0 ? string.Empty : url[hashIndex..];
        if (!path.EndsWith(".md", StringComparison.Ordinal))
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var segment in $"{page.Directory}/{path[..^3]}".Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        if (segments.Count > 0 && segments[^1] == "index")
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return PageUrl(string.Join('/', segments)) + fragment;
    }

    private static void MarkExternal(LinkInline link, string cssClass)
    {
        var attributes = link.GetAttributes();
        attributes.AddClass(cssClass);
        attributes.AddPropertyIfNotExist("target", "_blank");
        attributes.AddPropertyIfNotExist("rel", "noopener");
    }

    private static string InlineText(ContainerInline? inline) =>
        inline is null
            ? string.Empty
            : string.Concat(inline.Descendants<Inline>().Select(i => i switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                _ => string.Empty
            }));

    [GeneratedRegex(@"^\{\{auto:([a-z-]+)\}\}\s*$", RegexOptions.Multiline)]
    private static partial Regex AutoBlockPattern();

    [GeneratedRegex("^(?:sha-)?([0-9a-f]{7,40})$")]
    private static partial Regex CommitPattern();
}

public sealed record DocumentationHeading(string Id, string Text);

public sealed record RenderedDocumentationPage(string Html, IReadOnlyList<DocumentationHeading> Headings);

/// <summary>Nastavení příručky (sekce Documentation v appsettings).</summary>
public sealed class DocumentationOptions
{
    public const string SectionName = "Documentation";

    /// <summary>Složka s Markdown soubory (relativně ke ContentRoot). Prázdná = <c>prirucka</c> vedle DLL (build, image).</summary>
    public string? ContentPath { get; set; }

    public string RepositoryUrl { get; set; } = "https://github.com/ftichy/MiniShop";

    /// <summary>Commit, ze kterého aplikace běží („sha-…“ z IMAGE_TAG). Jiná hodnota = odkazy na main.</summary>
    public string? Commit { get; set; }
}
