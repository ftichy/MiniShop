namespace MiniShop.Contracts.Orders;

/// <summary>E-shop krok 7: objednávku nešlo vyřídit (málo zboží, sklad neodpověděl včas). Čtou notifikace.</summary>
public sealed record OrderRejectedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string Reason) : IIntegrationEvent;
