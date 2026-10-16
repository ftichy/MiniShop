using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>Objednávka byla zrušena.</summary>
public sealed record OrderCancelled(Order Order, DateTime OccurredAtUtc) : IDomainEvent;
