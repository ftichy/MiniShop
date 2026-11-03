namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 7: objednávka byla vytvořena – celý snímek pro odběratele, kteří chtějí zákazníkovi psát (Notifications).
/// Expand → contract: Ordering posílá zatím V1 i V2; V1 zůstává pro stávající odběratele.
/// </summary>
/// <remarks>
/// Nese osobní údaje (jméno, e-mail, adresu) – odběratel, který si je uloží, musí poslouchat i <see cref="Customers.CustomerAnonymizedV1"/>.
/// </remarks>
/// <param name="ShippingMethod"><c>None</c> (bez pokladny / API), <c>Pickup</c>, <c>Courier</c>.</param>
/// <param name="DeliveryAddress">Adresa jedním řádkem (jen kurýr).</param>
public sealed record OrderPlacedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerId,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyList<OrderItemV2> Items,
    string ShippingMethod,
    decimal ShippingPrice,
    string? DeliveryAddress,
    decimal TotalPrice) : IIntegrationEvent;

/// <summary>Položka objednávky ve snímku <see cref="OrderPlacedV2"/> – název a ceny z doby objednání.</summary>
public sealed record OrderItemV2(int ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Discount, decimal LineTotal);
