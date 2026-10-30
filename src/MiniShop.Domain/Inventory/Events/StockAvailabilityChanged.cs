using MiniShop.Domain.Common;

namespace MiniShop.Domain.Inventory.Events;

/// <summary>
/// E-shop krok 2: produkt přešel do jiného pásma dostupnosti (ne při každé změně zásoby – jen při změně pásma).
/// Drží odkaz na produkt jako <see cref="ProductChanged"/> – zpráva se sestaví až při uložení (nový produkt má pak ID).
/// </summary>
public sealed record StockAvailabilityChanged(Product Product, StockAvailability Availability, DateTime OccurredAtUtc)
    : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
