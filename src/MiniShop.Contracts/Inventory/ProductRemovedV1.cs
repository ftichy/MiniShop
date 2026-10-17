namespace MiniShop.Contracts.Inventory;

/// <summary>Produkt byl odebrán z nabídky (krok 10) – už nejde objednat.</summary>
public sealed record ProductRemovedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int ProductId) : IIntegrationEvent;
