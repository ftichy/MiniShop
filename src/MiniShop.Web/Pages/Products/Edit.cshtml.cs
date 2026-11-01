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
/// E-shop krok 2B: nahrání a odebrání obrázku (soubor jde do skladu, do výlohy jen jeho jméno).
/// </summary>
/// <remarks>
/// Limit velikosti požadavku je o kousek vyšší než limit obrázku – větší soubor server ani nedočte (413),
/// o něco menší překročení ohlásí stránka srozumitelnou hláškou.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
[RequestSizeLimit(ProductImageFormat.MaxBytes + 256 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = ProductImageFormat.MaxBytes + 256 * 1024)]
public sealed class EditModel(
    IProductService productService,
    ICategoryService categoryService,
    IProductImageService imageService) : PageModel
{
    [BindProperty]
    public UpdateProductRequest Product { get; set; } = new();

    public IReadOnlyList<SelectListItem> CategoryOptions { get; private set; } = [];

    /// <summary>Jméno souboru aktuálního obrázku (null = bez obrázku).</summary>
    public string? ImageFileName { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await LoadProductAsync(id, cancellationToken);
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

        await LoadImageAsync(id, cancellationToken);
        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }

    // ---------- E-shop krok 2B: obrázek ----------

    public Task<IActionResult> OnPostImageAsync(int id, IFormFile? image, CancellationToken cancellationToken) =>
        ChangeImageAsync(id, async () =>
        {
            if (image is null || image.Length == 0)
            {
                throw new DomainException("Vyberte soubor s obrázkem.");
            }

            if (image.Length > ProductImageFormat.MaxBytes)
            {
                throw new DomainException("Obrázek může mít nejvýš 1 MB.");
            }

            using var content = new MemoryStream();
            await image.CopyToAsync(content, cancellationToken);
            await imageService.SetImageAsync(id, content.ToArray(), cancellationToken);
            StatusMessage = "Obrázek byl nahrán. Ve výloze bude za pár sekund.";
        }, cancellationToken);

    public Task<IActionResult> OnPostRemoveImageAsync(int id, CancellationToken cancellationToken) =>
        ChangeImageAsync(id, async () =>
        {
            await imageService.RemoveImageAsync(id, cancellationToken);
            StatusMessage = "Obrázek byl odebrán.";
        }, cancellationToken);

    /// <summary>
    /// Formulář obrázku neposílá pole produktu – [BindProperty] Product se pak naváže bez prefixu a jeho (prázdná)
    /// pole by hlásila chyby (stejná past jako u kategorií v kroku 2A). Proto ModelState.Clear a vlastní kontroly.
    /// </summary>
    private async Task<IActionResult> ChangeImageAsync(int id, Func<Task> action, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        try
        {
            await action();
            return RedirectToPage(new { id });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is DomainException or InventoryUnavailableException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        try
        {
            await LoadProductAsync(id, cancellationToken);
        }
        catch (Exception ex) when (ex is NotFoundException or InventoryUnavailableException)
        {
            // Chybu už stránka ukazuje – jen bez předvyplněného formuláře.
        }

        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }

    private async Task LoadProductAsync(int id, CancellationToken cancellationToken)
    {
        var product = await productService.GetByIdAsync(id, cancellationToken);
        Product = new UpdateProductRequest
        {
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            CategoryId = product.CategoryId
        };
        ImageFileName = product.ImageFileName;
    }

    /// <summary>Po neúspěšném uložení formuláře: obrázek pro náhled (formulář si nechá, co uživatel napsal).</summary>
    private async Task LoadImageAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            ImageFileName = (await productService.GetByIdAsync(id, cancellationToken)).ImageFileName;
        }
        catch (Exception ex) when (ex is NotFoundException or InventoryUnavailableException)
        {
        }
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
