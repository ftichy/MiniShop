namespace MiniShop.Contracts.Orders;

/// <summary>E-shop krok 7: sklad zboží zarezervoval, objednávka je potvrzená. Čtou notifikace.</summary>
public sealed record OrderConfirmedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId) : IIntegrationEvent;
