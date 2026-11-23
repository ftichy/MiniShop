using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Inventory;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Accounts;
using MiniShop.Web.Inventory;
using MiniShop.Web.Ui;

namespace MiniShop.Web.Pages.Products;

/// <summary>
/// Správa produktů. Krok 10B: produkty patří službě skladu – <see cref="IProductService"/> je tu
/// <see cref="InventoryApiClient"/>, který volá její HTTP API. E-shop krok 2: i kategorie (<see cref="ICategoryService"/>).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireStock)]   // účty krok 12B: přehled a naskladnění (sortiment i sklad), úpravy jen sortiment – kontrola v handlerech
public sealed class IndexModel(IProductService productService, ICategoryService categoryService, IAuthorizationService authorization)
    : PageModel
{
    /// <summary>Účty krok 12B: zakládat, mazat a kategorie smí jen správce sortimentu (sklad jen naskladňuje).</summary>
    public bool CanEditCatalog { get; private set; }

    public PagedList<ProductDto> ProductPage { get; private set; } = new([], 1, PageRequest.DefaultSize, 0);
    public IReadOnlyList<ProductDto> Products => ProductPage.Items;
    public PagerModel Pager => PagerModel.From(ProductPage, "products");

    [BindProperty(SupportsGet = true, Name = PagerModel.PageParameter)]
    public int? PageNumber { get; set; }

    [BindProperty(SupportsGet = true, Name = PagerModel.SizeParameter)]
    public int? PageSize { get; set; }

    /// <summary>E-shop krok 28A: sklady – sloupec zásob po skladech a volba skladu při naskladnění.</summary>
    public IReadOnlyList<WarehouseDto> Warehouses { get; private set; } = [];

    /// <summary>Kolik kusů produktu je ve skladu (sklad bez kusů v DTO chybí).</summary>
    public static int StockIn(ProductDto product, int warehouseId) =>
        product.Stocks?.FirstOrDefault(s => s.WarehouseId == warehouseId)?.Quantity ?? 0;

    public IReadOnlyList<CategoryDto> Categories { get; private set; } = [];

    public IReadOnlyList<SelectListItem> CategoryOptions =>
        Categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();

    [BindProperty]
    public CreateProductRequest NewProduct { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!await IsCatalogEditorAsync())
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return await ReloadAsync(cancellationToken);
        }

        return await ExecuteAsync(async () =>
        {
            var product = await productService.CreateAsync(NewProduct, cancellationToken);
            StatusMessage = $"Produkt '{product.Name}' byl přidán.";
        }, cancellationToken);
    }

    /// <param name="warehouseId">E-shop krok 28A: do kterého skladu (chybí = výchozí Praha).</param>
    public Task<IActionResult> OnPostAddStockAsync(int id, int quantity, int? warehouseId, CancellationToken cancellationToken)
    {
        ModelState.Clear(); // formulář NewProduct se tu neodesílá
        return ExecuteAsync(async () =>
        {
            var product = await productService.AddStockAsync(id, quantity, warehouseId, cancellationToken);
            var warehouse = warehouseId is null ? null
                : (await productService.GetWarehousesAsync(cancellationToken)).FirstOrDefault(w => w.Id == warehouseId)?.Name;
            StatusMessage = $"Naskladněno {quantity} ks produktu '{product.Name}'" + (warehouse is null ? "." : $" do skladu {warehouse}.");
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!await IsCatalogEditorAsync())
        {
            return Forbid();
        }

        ModelState.Clear();
        return await ExecuteAsync(async () =>
        {
            await productService.DeleteAsync(id, cancellationToken);
            StatusMessage = "Produkt byl smazán.";
        }, cancellationToken);
    }

    // ---------- E-shop krok 2: kategorie ----------

    public async Task<IActionResult> OnPostCreateCategoryAsync(CategoryRequest category, CancellationToken cancellationToken)
    {
        if (!await IsCatalogEditorAsync())
        {
            return Forbid();
        }

        if (!IsCategoryValid(category))
        {
            return await ReloadAsync(cancellationToken);
        }

        return await ExecuteAsync(async () =>
        {
            var created = await categoryService.CreateAsync(category, cancellationToken);
            StatusMessage = $"Kategorie '{created.Name}' byla přidána.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostRenameCategoryAsync(int id, CategoryRequest category, CancellationToken cancellationToken)
    {
        if (!await IsCatalogEditorAsync())
        {
            return Forbid();
        }

        if (!IsCategoryValid(category))
        {
            return await ReloadAsync(cancellationToken);
        }

        return await ExecuteAsync(async () =>
        {
            var renamed = await categoryService.RenameAsync(id, category, cancellationToken);
            StatusMessage = $"Kategorie přejmenována na '{renamed.Name}'. Výloha se aktualizuje během pár sekund.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id, CancellationToken cancellationToken)
    {
        if (!await IsCatalogEditorAsync())
        {
            return Forbid();
        }

        ModelState.Clear();
        return await ExecuteAsync(async () =>
        {
            await categoryService.DeleteAsync(id, cancellationToken);
            StatusMessage = "Kategorie byla smazána.";
        }, cancellationToken);
    }

    /// <summary>
    /// Formulář nového produktu se u kategorií neodesílá – jeho (prázdná) pole nejsou chyba. Pozor: když ve formuláři
    /// žádné pole <c>NewProduct.*</c> není, binder naváže [BindProperty] BEZ prefixu (klíče „Name“, „Price“…), takže jeho
    /// chyby nejdou poznat podle klíče. Proto vše smazat a znovu ověřit jen kategorii; chyby nahoru do souhrnu.
    /// </summary>
    private bool IsCategoryValid(CategoryRequest category)
    {
        ModelState.Clear();
        if (TryValidateModel(category, nameof(category)))
        {
            return true;
        }

        var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        ModelState.Clear();
        errors.ForEach(error => ModelState.AddModelError(string.Empty, error));
        return false;
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
            return RedirectToPage(new { p = PageNumber, size = PageSize });   // zpět na stejnou stránku seznamu
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException or InventoryUnavailableException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    private async Task<IActionResult> ReloadAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task<bool> IsCatalogEditorAsync() =>
        (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCatalog)).Succeeded;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanEditCatalog = await IsCatalogEditorAsync();
        try
        {
            // Produkty jdou ze skladu přes API celé (jsou jich desítky) – stránka se vybere tady, API se nemění.
            ProductPage = (await productService.GetAllAsync(cancellationToken)).ToPagedList(PageRequest.Of(PageNumber, PageSize));
            Categories = await categoryService.GetAllAsync(cancellationToken);
            Warehouses = await productService.GetWarehousesAsync(cancellationToken);
        }
        catch (InventoryUnavailableException ex)
        {
            // Sklad neběží – stránka se ukáže s hláškou místo chyby 500.
            ProductPage = new([], 1, PageRequest.DefaultSize, 0);
            Categories = [];
            Warehouses = [];
            var alreadyShown = ModelState[string.Empty]?.Errors.Any(e => e.ErrorMessage == ex.Message) ?? false;
            if (!alreadyShown)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }
    }
}
