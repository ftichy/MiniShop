using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Inventory;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Inventory;

namespace MiniShop.Web.Pages.Products;

/// <summary>
/// Správa produktů. Krok 10B: produkty patří službě skladu – <see cref="IProductService"/> je tu
/// <see cref="InventoryApiClient"/>, který volá její HTTP API.
/// </summary>
public sealed class IndexModel(IProductService productService) : PageModel
{
    public IReadOnlyList<ProductDto> Products { get; private set; } = [];

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
        }
        catch (InventoryUnavailableException ex)
        {
            // Sklad neběží – stránka se ukáže s hláškou místo chyby 500.
            Products = [];
            var alreadyShown = ModelState[string.Empty]?.Errors.Any(e => e.ErrorMessage == ex.Message) ?? false;
            if (!alreadyShown)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }
    }
}
