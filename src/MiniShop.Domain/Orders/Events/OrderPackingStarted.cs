using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 6: sklad začal objednávku balit (po tomhle už jen odeslat, nebo ještě zrušit).</summary>
public sealed record OrderPackingStarted(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
