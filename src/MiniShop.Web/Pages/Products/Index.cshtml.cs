using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniShop.Application.Inventory;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Accounts;
using MiniShop.Web.Inventory;

namespace MiniShop.Web.Pages.Products;

/// <summary>
/// Správa produktů. Krok 10B: produkty patří službě skladu – <see cref="IProductService"/> je tu
/// <see cref="InventoryApiClient"/>, který volá její HTTP API. E-shop krok 2: i kategorie (<see cref="ICategoryService"/>).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]   // účty krok 2: sklad a produkty spravuje jen admin
public sealed class IndexModel(IProductService productService, ICategoryService categoryService) : PageModel
{
    public IReadOnlyList<ProductDto> Products { get; private set; } = [];

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

    public Task<IActionResult> OnPostAddStockAsync(int id, int quantity, CancellationToken cancellationToken)
    {
        ModelState.Clear(); // formulář NewProduct se tu neodesílá
        return ExecuteAsync(async () =>
        {
            var product = await productService.AddStockAsync(id, quantity, cancellationToken);
            StatusMessage = $"Naskladněno {quantity} ks produktu '{product.Name}'.";
        }, cancellationToken);
    }

    public Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return ExecuteAsync(async () =>
        {
            await productService.DeleteAsync(id, cancellationToken);
            StatusMessage = "Produkt byl smazán.";
        }, cancellationToken);
    }

    // ---------- E-shop krok 2: kategorie ----------

    public async Task<IActionResult> OnPostCreateCategoryAsync(CategoryRequest category, CancellationToken cancellationToken)
    {
        if (!IsCategoryValid())
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
        if (!IsCategoryValid())
        {
            return await ReloadAsync(cancellationToken);
        }

        return await ExecuteAsync(async () =>
        {
            var renamed = await categoryService.RenameAsync(id, category, cancellationToken);
            StatusMessage = $"Kategorie přejmenována na '{renamed.Name}'. Výloha se aktualizuje během pár sekund.";
        }, cancellationToken);
    }

    public Task<IActionResult> OnPostDeleteCategoryAsync(int id, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return ExecuteAsync(async () =>
        {
            await categoryService.DeleteAsync(id, cancellationToken);
            StatusMessage = "Kategorie byla smazána.";
        }, cancellationToken);
    }

    /// <summary>Formulář nového produktu se u kategorií neodesílá – jeho (prázdná) pole nejsou chyba.</summary>
    private bool IsCategoryValid()
    {
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith(nameof(NewProduct), StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }

        return ModelState.IsValid;
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
            return RedirectToPage();
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Products = await productService.GetAllAsync(cancellationToken);
            Categories = await categoryService.GetAllAsync(cancellationToken);
        }
        catch (InventoryUnavailableException ex)
        {
            // Sklad neběží – stránka se ukáže s hláškou místo chyby 500.
            Products = [];
            Categories = [];
            var alreadyShown = ModelState[string.Empty]?.Errors.Any(e => e.ErrorMessage == ex.Message) ?? false;
            if (!alreadyShown)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }
    }
}
