using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Contracts;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Services;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Web.Pages.Products;

public sealed class IndexModel(IProductService productService) : PageModel
{
    public IReadOnlyList<ProductDto> Products { get; private set; } = [];

    [BindProperty]
    public CreateProductRequest NewProduct { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Products = await productService.GetAllAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ReloadAsync(cancellationToken);
        }

        try
        {
            var product = await productService.CreateAsync(NewProduct, cancellationToken);
            StatusMessage = $"Produkt '{product.Name}' byl přidán.";
            return RedirectToPage();
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostAddStockAsync(int id, int quantity, CancellationToken cancellationToken)
    {
        ModelState.Clear(); // formulář NewProduct se tu neodesílá
        try
        {
            var product = await productService.AddStockAsync(id, quantity, cancellationToken);
            StatusMessage = $"Naskladněno {quantity} ks produktu '{product.Name}'.";
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        try
        {
            await productService.DeleteAsync(id, cancellationToken);
            StatusMessage = "Produkt byl smazán.";
            return RedirectToPage();
        }
        catch (NotFoundException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    private async Task<IActionResult> ReloadAsync(CancellationToken cancellationToken)
    {
        Products = await productService.GetAllAsync(cancellationToken);
        return Page();
    }
}
