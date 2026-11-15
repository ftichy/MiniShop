using System.ComponentModel.DataAnnotations;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Application.Inventory;

/// <summary>Jeden produkt z importního souboru (<c>SeedData/products.json</c>). Obrázek = jméno souboru ve složce s obrázky.</summary>
public sealed record ProductImportItem(
    string Name,
    string? Description,
    decimal Price,
    int StockQuantity,
    string Category,
    string? Image);

/// <param name="Created">Nové produkty.</param>
/// <param name="Skipped">Produkty, které už ve skladu byly (podle názvu) – nic se nezdvojilo.</param>
/// <param name="CategoriesCreated">Nově založené kategorie.</param>
/// <param name="ImagesSet">Nahrané obrázky (nový produkt, nebo existující bez obrázku).</param>
public sealed record ProductImportResult(int Created, int Skipped, int CategoriesCreated, int ImagesSet);

/// <summary>
/// Jednorázový import produktů do skladu (příkaz <c>import-products</c> v Inventory.Api). Jede přes stejné služby jako
/// administrace (<see cref="IProductService"/>, <see cref="ICategoryService"/>, <see cref="IProductImageService"/>),
/// takže se vygenerují všechny zprávy a výloha ve Webu se doplní sama – import o Orderingu nic neví.
/// </summary>
/// <remarks>
/// Opakované spuštění nic nezdvojí: produkt se pozná podle názvu (bez ohledu na velikost písmen) a kategorie také.
/// Nejdřív se zkontroluje celý soubor (pravidla jako ve formuláři, existence a velikost obrázků), teprve pak se zapisuje –
/// chyba v 25. řádku tak nenechá sklad napůl naplněný.
/// </remarks>
public sealed class ProductImportService(
    IProductService products,
    ICategoryService categories,
    IProductImageService images)
{
    public async Task<ProductImportResult> ImportAsync(
        IReadOnlyList<ProductImportItem> items, string imageDirectory, CancellationToken cancellationToken = default)
    {
        var imageContents = Validate(items, imageDirectory);

        var existingCategories = (await categories.GetAllAsync(cancellationToken))
            .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var existingProducts = (await products.GetAllAsync(cancellationToken))
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        int created = 0, skipped = 0, categoriesCreated = 0, imagesSet = 0;

        foreach (var item in items)
        {
            if (!existingCategories.TryGetValue(item.Category, out var categoryId))
            {
                var category = await categories.CreateAsync(new CategoryRequest { Name = item.Category }, cancellationToken);
                categoryId = category.Id;
                existingCategories[item.Category] = categoryId;
                categoriesCreated++;
            }

            int productId;
            string? currentImage;
            if (existingProducts.TryGetValue(item.Name, out var existing))
            {
                skipped++;
                productId = existing.Id;
                currentImage = existing.ImageFileName;
            }
            else
            {
                var product = await products.CreateAsync(new CreateProductRequest
                {
                    Name = item.Name,
                    Description = item.Description,
                    Price = item.Price,
                    StockQuantity = item.StockQuantity,
                    CategoryId = categoryId,
                }, cancellationToken);
                created++;
                productId = product.Id;
                currentImage = null;
            }

            if (currentImage is null && imageContents.TryGetValue(item.Name, out var content))
            {
                await images.SetImageAsync(productId, content, cancellationToken);
                imagesSet++;
            }
        }

        return new ProductImportResult(created, skipped, categoriesCreated, imagesSet);
    }

    /// <summary>Zkontroluje celý soubor dřív, než se cokoli zapíše. Vrací obsah obrázků podle názvu produktu.</summary>
    private static Dictionary<string, byte[]> Validate(IReadOnlyList<ProductImportItem> items, string imageDirectory)
    {
        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var label = string.IsNullOrWhiteSpace(item.Name) ? "(bez názvu)" : item.Name;

            if (!names.Add(item.Name ?? string.Empty))
            {
                errors.Add($"{label}: název je v souboru dvakrát.");
            }

            if (string.IsNullOrWhiteSpace(item.Category) || item.Category.Length > 50)
            {
                errors.Add($"{label}: kategorie je povinná (nejvýš 50 znaků).");
            }

            var request = new CreateProductRequest
            {
                Name = item.Name ?? string.Empty,
                Description = item.Description,
                Price = item.Price,
                StockQuantity = item.StockQuantity,
                CategoryId = 1,   // ID se doplní až při zápisu; tady se ověřují jen ostatní pole
            };
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
            {
                errors.AddRange(results.Select(r => $"{label}: {r.ErrorMessage}"));
            }

            if (string.IsNullOrWhiteSpace(item.Image))
            {
                continue;
            }

            // Jen jméno souboru – žádné cesty (../), obrázky se berou výhradně ze složky importu.
            if (item.Image != Path.GetFileName(item.Image))
            {
                errors.Add($"{label}: obrázek musí být jen jméno souboru.");
                continue;
            }

            var path = Path.Combine(imageDirectory, item.Image);
            if (!File.Exists(path))
            {
                errors.Add($"{label}: obrázek {item.Image} neexistuje.");
                continue;
            }

            var content = File.ReadAllBytes(path);
            if (content.Length > ProductImageFormat.MaxBytes || ProductImageFormat.DetectExtension(content) is null)
            {
                errors.Add($"{label}: obrázek {item.Image} není JPEG/PNG/WebP do 1 MB.");
                continue;
            }

            contents[item.Name!] = content;
        }

        if (errors.Count > 0)
        {
            throw new DomainException("Import se nezapsal, soubor obsahuje chyby:" + Environment.NewLine +
                                      string.Join(Environment.NewLine, errors));
        }

        return contents;
    }
}
