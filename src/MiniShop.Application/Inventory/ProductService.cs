using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Správa produktů – modul Inventory (krok 10).
/// </summary>
/// <remarks>
/// Založení, změna i smazání produktu vyvolá doménovou událost (ProductChanged / ProductRemoved).
/// InventoryDbContext ji při uložení rovnou převede na integrační ProductUpdatedV1 + V2 / ProductRemovedV1 do outboxu –
/// tak se o změně dozví Ordering a aktualizuje si kopii katalogu.
/// </remarks>
public sealed class ProductService(
    IProductRepository products,
    ICategoryRepository categories,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IProductService
{
    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await products.GetAllAsync(cancellationToken);
        return all.Select(p => p.ToDto()).ToList();
    }

    public async Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        return product.ToDto();
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var category = await GetCategoryAsync(request.CategoryId, cancellationToken);
        var product = new Product(request.Name, request.Description, request.Price, category, request.StockQuantity, UtcNow);
        products.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ProductUpdatedV1 + V2 do outboxu
        return product.ToDto();
    }

    public async Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        var category = await GetCategoryAsync(request.CategoryId, cancellationToken);
        product.Update(request.Name, request.Description, request.Price, category, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ProductUpdatedV1 + V2 do outboxu
        return product.ToDto();
    }

    public async Task<ProductDto> AddStockAsync(int id, int quantity, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        product.AddStock(quantity, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // zpráva jen při změně pásma dostupnosti (StockAvailabilityChangedV1)
        return product.ToDto();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        product.MarkRemoved(UtcNow);
        products.Remove(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ProductRemovedV1 do outboxu
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<Product> GetProductAsync(int id, CancellationToken cancellationToken) =>
        await products.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Produkt s ID {id} nebyl nalezen.");

    // Neexistující kategorie = chyba vstupu (400), ne "nenalezeno" (404) – nenalezený je tu produkt, ne kategorie.
    private async Task<Category> GetCategoryAsync(int categoryId, CancellationToken cancellationToken) =>
        await categories.GetByIdAsync(categoryId, cancellationToken)
        ?? throw new DomainException($"Kategorie s ID {categoryId} neexistuje.");
}
