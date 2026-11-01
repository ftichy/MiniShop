using MiniShop.Domain.Common;

namespace MiniShop.Domain.Inventory.Events;

/// <summary>Produkt byl založen nebo se změnil jeho název, popis, cena nebo kategorie (krok 10, e-shop krok 2).</summary>
public sealed record ProductChanged(Product Product, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
