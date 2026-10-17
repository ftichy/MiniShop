using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>
/// Objednávka byla vytvořena. Nese odkaz na entitu – doménová událost neopouští aplikaci.
/// Ven se posílá integrační OrderPlacedV1 z MiniShop.Contracts (krok 7).
/// </summary>
public sealed record OrderPlaced(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
