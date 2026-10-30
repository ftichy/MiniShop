using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Shop;

/// <summary>
/// E-shop krok 2: výloha – veřejná nabídka s hledáním, filtrem kategorie a dostupnosti, řazením a stránkováním.
/// Čte jen kopii katalogu v Orderingu (<see cref="ICatalogQueryService"/>), takže funguje i při výpadku skladu.
/// </summary>
/// <remarks>
/// Filtry jsou v URL (formulář GET) – výsledek jde poslat odkazem, funguje tlačítko Zpět a nepotřebuje JavaScript.
/// </remarks>
[AllowAnonymous]
public sealed class IndexModel(ICatalogQueryService catalog, IAuthorizationService authorization) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "category")]
    public int? CategoryId { get; set; }

    [BindProperty(SupportsGet = true, Name = "instock")]
    public bool InStockOnly { get; set; }

    [BindProperty(SupportsGet = true, Name = "sort")]
    public CatalogSort Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public CatalogSearchResult Result { get; private set; } = new([], 0, 1, 1, []);

    /// <summary>Objednávat smí zákazník s potvrzeným e-mailem (účty krok 9) – jiným tlačítko nenabízíme.</summary>
    public bool CanOrder { get; private set; }

    public bool IsAnonymous => User.Identity?.IsAuthenticated != true;

    public bool HasFilter => !string.IsNullOrWhiteSpace(Query) || CategoryId is not null || InStockOnly;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Nesmyslné hodnoty z URL (ruční úprava) jen ignorujeme – výloha nemá padat.
        if (!ModelState.IsValid)
        {
            ModelState.Clear();
            CategoryId = null;
            Sort = CatalogSort.Name;
            PageNumber = 1;
        }

        CanOrder = (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCustomer)).Succeeded;
        Result = await catalog.SearchAsync(
            new CatalogSearchRequest(Query?.Trim(), CategoryId, InStockOnly, Sort, PageNumber), cancellationToken);
    }

    /// <summary>Parametry aktuálního hledání pro odkaz na jinou stránku výsledků.</summary>
    public Dictionary<string, string> RouteFor(int page)
    {
        var values = new Dictionary<string, string> { ["p"] = page.ToString() };
        if (!string.IsNullOrWhiteSpace(Query)) values["q"] = Query.Trim();
        if (CategoryId is not null) values["category"] = CategoryId.Value.ToString();
        if (InStockOnly) values["instock"] = "true";
        if (Sort != CatalogSort.Name) values["sort"] = Sort.ToString();
        return values;
    }
}
