using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Web.Accounts;
using MiniShop.Web.Catalog;

namespace MiniShop.Web.Pages;

/// <summary>
/// Redesign A: úvod pro zákazníka – hlavní nabídka, kategorie a pár doporučených produktů (nejlépe hodnocené skladem)
/// z kopie katalogu. Veřejná stránka (účty krok 2) – technické odkazy jsou v patičce a v Dokumentaci.
/// </summary>
[AllowAnonymous]
public sealed class IndexModel(ICatalogQueryService catalog, IAuthorizationService authorization) : PageModel
{
    public const int FeaturedCount = 4;

    public IReadOnlyList<CatalogItemDto> Featured { get; private set; } = [];

    public IReadOnlyList<CatalogCategoryDto> Categories { get; private set; } = [];

    public bool CanOrder { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanOrder = (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCustomer)).Succeeded;
        var result = await catalog.SearchAsync(new CatalogSearchRequest(InStockOnly: true, Sort: CatalogSort.BestRated), cancellationToken);
        Featured = result.Items.Take(FeaturedCount).ToList();
        Categories = result.Categories;
    }

    public ProductCardModel Card(CatalogItemDto product) =>
        new(product, CanOrder, User.Identity?.IsAuthenticated != true, "/");
}
