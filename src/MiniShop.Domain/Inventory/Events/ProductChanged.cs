using MiniShop.Domain.Common;

namespace MiniShop.Domain.Inventory.Events;

/// <summary>Produkt byl založen nebo se změnil jeho název / cena (krok 10).</summary>
public sealed record ProductChanged(Product Product, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
