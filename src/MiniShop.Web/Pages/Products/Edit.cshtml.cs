using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Accounts;
using MiniShop.Web.Inventory;

namespace MiniShop.Web.Pages.Products;

/// <summary>
/// E-shop krok 2: úprava produktu (název, popis, cena, kategorie). Zásoba se tu nemění – na to je naskladnění.
/// Sklad po uložení pošle ProductUpdatedV1 + V2, výloha se aktualizuje za pár sekund.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class EditModel(IProductService productService, ICategoryService categoryService) : PageModel
{
    [BindProperty]
    public UpdateProductRequest Product { get; set; } = new();

    public IReadOnlyList<SelectListItem> CategoryOptions { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var product = await productService.GetByIdAsync(id, cancellationToken);
            Product = new UpdateProductRequest
            {
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                CategoryId = product.CategoryId
            };
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (InventoryUnavailableException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var product = await productService.UpdateAsync(id, Product, cancellationToken);
                StatusMessage = $"Produkt '{product.Name}' byl upraven. Výloha se aktualizuje během pár sekund.";
                return RedirectToPage("/Products/Index");
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex) when (ex is DomainException or InventoryUnavailableException)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            CategoryOptions = (await categoryService.GetAllAsync(cancellationToken))
                .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
                .ToList();
        }
        catch (InventoryUnavailableException)
        {
            CategoryOptions = [];   // hlášku už ukazuje chyba produktu nebo uložení
        }
    }
}
