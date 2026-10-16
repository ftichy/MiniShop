using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>Objednávka byla vyřízena.</summary>
public sealed record OrderCompleted(Order Order, DateTime OccurredAtUtc) : IDomainEvent;
