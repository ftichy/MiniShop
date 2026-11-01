using System.Globalization;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// E-shop krok 2: správa kategorií. Kategorie samy žádnou zprávu neposílají – Ordering je zná jen ze zpráv o produktech.
/// </summary>
/// <remarks>
/// Kategorií je pár, proto se názvy porovnávají a řadí v paměti s českými pravidly (SQLite umí bez rozlišení
/// velikosti písmen jen ASCII – „Šperky“ a „šperky“ by nepoznal).
/// </remarks>
public sealed class CategoryService(
    ICategoryRepository categories,
    IProductRepository products,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICategoryService
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await categories.GetAllAsync(cancellationToken);
        var counts = await categories.CountProductsAsync(cancellationToken);
        return all
            .OrderBy(c => c.Name, StringComparer.Create(Czech, ignoreCase: true))
            .Select(c => new CategoryDto(c.Id, c.Name, counts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureUniqueNameAsync(request.Name, exceptId: null, cancellationToken);
        var category = new Category(request.Name);
        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CategoryDto(category.Id, category.Name, 0);
    }

    public async Task<CategoryDto> RenameAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await GetCategoryAsync(id, cancellationToken);
        await EnsureUniqueNameAsync(request.Name, exceptId: id, cancellationToken);
        category.Rename(request.Name);

        // Jméno kategorie jde ve zprávě o produktu – výloha se o přejmenování dozví od každého jejího produktu.
        var inCategory = await products.GetByCategoryAsync(id, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var product in inCategory)
        {
            product.CategoryRenamed(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ProductUpdatedV1 + V2 pro každý produkt
        return new CategoryDto(category.Id, category.Name, inCategory.Count);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await GetCategoryAsync(id, cancellationToken);
        var count = (await products.GetByCategoryAsync(id, cancellationToken)).Count;
        if (count > 0)
        {
            throw new DomainException($"Kategorii {category.Name} nejde smazat – má {count} produktů. Nejdřív je přeřaďte.");
        }

        categories.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUniqueNameAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        var all = await categories.GetAllAsync(cancellationToken);
        if (all.Any(c => c.Id != exceptId && string.Compare(c.Name, name.Trim(), Czech, CompareOptions.IgnoreCase) == 0))
        {
            throw new DomainException($"Kategorie {name.Trim()} už existuje.");
        }
    }

    private async Task<Category> GetCategoryAsync(int id, CancellationToken cancellationToken) =>
        await categories.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Kategorie s ID {id} nebyla nalezena.");
}
