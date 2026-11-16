using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 22: vratka po částech je hotová – kusy zpět na skladu, peníze vrácené (kartou) nebo je vrací admin.</summary>
public sealed record OrderPartialReturnCompleted(Order Order, int ReturnNumber, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
