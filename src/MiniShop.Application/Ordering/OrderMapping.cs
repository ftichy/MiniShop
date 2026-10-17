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
}
