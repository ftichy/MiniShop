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
            order.RejectionReason,
            order.ShippingMethod.ToString(),
            order.ShippingPrice,
            order.DeliveryAddress?.ToString(),
            order.Carrier,
            order.TrackingNumber,
            order.PaymentMethod.ToString(),
            order.CouponCode,          // e-shop krok 13
            order.CouponDiscount,
            order.IsAwaitingShipment,   // e-shop krok 26B: předává se dopravci
            order.ShipmentError,
            order.IsShippedByCarrier,   // e-shop krok 26C: doručení hlásí dopravce
            [.. order.EffectiveParcels().Select(p => new OrderParcelDto(p.Number, p.WarehouseCode, p.WarehouseName,
                [.. p.Lines.Select(l => new OrderParcelLineDto(l.ProductId,
                    order.Items.FirstOrDefault(i => i.ProductId == l.ProductId)?.ProductName ?? $"Produkt {l.ProductId}", l.Quantity))]))]);   // e-shop krok 28B

    /// <summary>E-shop krok 5: objednávka s historií stavů v pořadí, jak nastaly (podle Id – pořadí zápisu).</summary>
    public static OrderDetailDto ToDetailDto(this Order order) =>
        new(
            order.ToDto(),
            order.StatusHistory
                .OrderBy(c => c.Id)
                .Select(c => new OrderStatusChangeDto(c.Status.ToString(), c.ChangedAtUtc, c.Note))
                .ToList(),
            Returns: order.Returns.Count == 0 ? null : order.Returns.OrderBy(r => r.Number).Select(r => r.ToDto()).ToList(),
            CustomerId: order.CustomerId);

    /// <summary>E-shop krok 22: vratka po částech.</summary>
    public static OrderReturnDto ToDto(this OrderReturn orderReturn) =>
        new(orderReturn.Number, orderReturn.Status.ToString(), orderReturn.Reason, orderReturn.RequestedAtUtc,
            orderReturn.ResolvedAtUtc, orderReturn.RejectionReason, orderReturn.Amount,
            orderReturn.Lines.Select(l => new OrderReturnLineDto(l.ProductId, l.ProductName, l.Quantity, l.LineTotal)).ToList());

    /// <summary>E-shop krok 2: produkt z kopie katalogu pro výlohu i formulář objednávky.</summary>
    public static CatalogItemDto ToCatalogItemDto(this CatalogProduct product) =>
        new(product.Id, product.Name, product.Description, product.Price, product.CategoryId, product.CategoryName,
            product.Availability.ToString(), product.ImageFileName, product.RatingAverage, product.RatingCount);
}
