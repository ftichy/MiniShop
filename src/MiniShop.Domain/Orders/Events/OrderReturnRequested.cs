using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 12: zákazník chce doručenou objednávku vrátit (důvod v Order.ReturnReason) – startuje ságu vratky.</summary>
public sealed record OrderReturnRequested(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
