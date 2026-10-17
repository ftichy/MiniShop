namespace MiniShop.Contracts.Orders;

/// <summary>Objednávka byla vyřízena. Čtou např. notifikace.</summary>
public sealed record OrderCompletedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail) : IIntegrationEvent;
