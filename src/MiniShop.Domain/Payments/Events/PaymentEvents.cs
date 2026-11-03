using MiniShop.Domain.Common;

namespace MiniShop.Domain.Payments.Events;

/// <summary>E-shop krok 8: brána potvrdila platbu.</summary>
public sealed record PaymentSucceeded(Payment Payment, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>E-shop krok 8: brána platbu zamítla.</summary>
public sealed record PaymentFailed(Payment Payment, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>E-shop krok 8: peníze se vracejí (pozdní platba – objednávka už byla zamítnutá).</summary>
public sealed record PaymentRefunded(Payment Payment, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
