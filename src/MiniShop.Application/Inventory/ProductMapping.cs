using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Převod produktu na DTO. Doména se tak nikdy nevrací přímo z API.
/// </summary>
public static class ProductMapping
{
    public static ProductDto ToDto(this Product product) =>
        new(product.Id, product.Name, product.Description, product.Price, product.StockQuantity,
            product.CategoryId, product.Category.Name, product.ImageFileName);
}
