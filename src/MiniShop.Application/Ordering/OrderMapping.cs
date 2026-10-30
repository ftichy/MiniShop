using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

/// <summary>
/// Převod objednávky na DTO. Doména se tak nikdy nevrací přímo z API.
/// </summary>
public static class OrderMapping
{
    public static OrderDto ToDto(this Order order) =>
        new(
            order.Id,
            order.CustomerName,
            order.CustomerEmail,
            order.CreatedAtUtc,
            order.Status.ToString(),
            order.TotalPrice,
            order.Items
                .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.Discount, i.LineTotal))
                .ToList(),
            order.RejectionReason);

    /// <summary>E-shop krok 2: produkt z kopie katalogu pro výlohu i formulář objednávky.</summary>
    public static CatalogItemDto ToCatalogItemDto(this CatalogProduct product) =>
        new(product.Id, product.Name, product.Description, product.Price, product.CategoryId, product.CategoryName,
            product.Availability.ToString());
}
