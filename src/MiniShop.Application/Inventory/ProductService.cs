using MiniShop.Application.Exceptions;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Správa produktů – modul Inventory (krok 10).
/// </summary>
/// <remarks>
/// Založení, změna i smazání produktu vyvolá doménovou událost (ProductChanged / ProductRemoved).
/// InventoryDbContext ji při uložení rovnou převede na integrační ProductUpdatedV1 / ProductRemovedV1 do outboxu –
/// tak se o změně dozví Ordering a aktualizuje si kopii katalogu.
/// </remarks>
public sealed class ProductService(IProductRepository products, IInventoryUnitOfWork unitOfWork, TimeProvider timeProvider)
    : IProductService
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
        var product = new Product(request.Name, request.Description, request.Price, request.StockQuantity, UtcNow);
        products.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ProductUpdatedV1 do outboxu
        return product.ToDto();
    }

    public async Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        product.Update(request.Name, request.Description, request.Price, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ProductUpdatedV1 do outboxu
        return product.ToDto();
    }

    public async Task<ProductDto> AddStockAsync(int id, int quantity, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        product.AddStock(quantity);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // zásobu Ordering nepotřebuje – žádná zpráva
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
}
