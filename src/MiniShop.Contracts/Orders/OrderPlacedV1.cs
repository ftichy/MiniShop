namespace MiniShop.Contracts.Orders;

/// <summary>Objednávka byla vytvořena. Čte např. sklad (rezervace) a notifikace (potvrzení zákazníkovi).</summary>
public sealed record OrderPlacedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail,
    decimal TotalPrice,
    IReadOnlyList<OrderLineV1> Lines) : IIntegrationEvent;
