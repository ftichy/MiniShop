namespace MiniShop.Contracts.Orders;

/// <summary>Objednávka byla zrušena. Sklad podle položek vrátí zboží, notifikace pošlou oznámení.</summary>
public sealed record OrderCancelledV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail,
    IReadOnlyList<OrderLineV1> Lines) : IIntegrationEvent;
