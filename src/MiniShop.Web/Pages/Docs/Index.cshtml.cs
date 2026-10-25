using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Web.Documentation;

namespace MiniShop.Web.Pages.Docs;

/// <summary>Příručka v aplikaci: jedna stránka z docs/prirucka + strom pro navigaci.</summary>
public sealed class IndexModel(DocumentationStore store, DocumentationRenderer renderer) : PageModel
{
    public DocumentationPage Current { get; private set; } = null!;
    public RenderedDocumentationPage Rendered { get; private set; } = null!;
    public DocumentationNode? Tree { get; private set; }
    public IReadOnlyList<DocumentationPage> Ancestors { get; private set; } = [];
    public IReadOnlyList<DocumentationPage> Children { get; private set; } = [];

    public IActionResult OnGet(string? slug)
    {
        var page = store.Find(slug);
        if (page is null)
        {
            return NotFound();
        }

        Current = page;
        Rendered = renderer.Render(page);
        Tree = store.Tree();
        Ancestors = store.Ancestors(page.Slug);
        Children = FindNode(Tree, page.Slug)?.Children.Select(c => c.Page).ToList() ?? [];
        return Page();
    }

    /// <summary>Má se v navigaci rozbalit větev uzlu? Ano pro úvod, aktuální stránku a její předky.</summary>
    public bool IsExpanded(DocumentationNode node) =>
        node.Page.Slug.Length == 0
        || node.Page.Slug == Current.Slug
        || Current.Slug.StartsWith(node.Page.Slug + "/", StringComparison.Ordinal);

    private static DocumentationNode? FindNode(DocumentationNode? node, string slug)
    {
        if (node is null || node.Page.Slug == slug)
        {
            return node;
        }

        return node.Children.Select(child => FindNode(child, slug)).FirstOrDefault(found => found is not null);
    }
}
