using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>
/// Objednávka byla vytvořena. Nese odkaz na entitu – událost zatím neopouští proces.
/// (Pro události posílané ven to změníme v kroku 7.)
/// </summary>
public sealed record OrderPlaced(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
