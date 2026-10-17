using MiniShop.Domain.Common;

namespace MiniShop.Domain.Inventory.Events;

/// <summary>Produkt byl odebrán z nabídky (krok 10). Nese jen ID – entita po uložení už neexistuje.</summary>
public sealed record ProductRemoved(int ProductId, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
