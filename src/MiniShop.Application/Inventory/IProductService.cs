namespace MiniShop.Application.Inventory;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default);
    /// <param name="warehouseId">E-shop krok 28A: do kterého skladu; null = výchozí (Praha).</param>
    Task<ProductDto> AddStockAsync(int id, int quantity, int? warehouseId = null, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 28A: sklady (Praha, Brno) – pro volbu skladu při naskladnění.</summary>
    Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
