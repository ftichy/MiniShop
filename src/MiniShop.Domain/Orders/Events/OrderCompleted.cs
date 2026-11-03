using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>Objednávka byla doručena (e-shop krok 6; dřív „vyřízena“ – jméno zůstává kvůli kontraktu OrderCompletedV1).</summary>
public sealed record OrderCompleted(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
