using MiniShop.Application.Contracts;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;

namespace MiniShop.Application.Mapping;

/// <summary>
/// Převod doménových objektů na DTO. Doména se tak nikdy nevrací přímo z API.
/// </summary>
public static class MappingExtensions
{
    public static ProductDto ToDto(this Product product) =>
        new(product.Id, product.Name, product.Description, product.Price, product.StockQuantity);

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
                .ToList());
}
