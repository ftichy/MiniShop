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

/// <summary>
/// E-shop krok 8: peníze se vracejí celé (pozdní platba, zrušení, vratka celé objednávky).
/// <paramref name="Amount"/> = kolik se vrátilo (od e-shop kroku 22 zbytek po případných částečných vráceních).
/// </summary>
public sealed record PaymentRefunded(Payment Payment, decimal Amount, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>E-shop krok 22: vrácena část peněz za jednu vratku zboží.</summary>
public sealed record PaymentPartlyRefunded(Payment Payment, PaymentRefund Refund, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
