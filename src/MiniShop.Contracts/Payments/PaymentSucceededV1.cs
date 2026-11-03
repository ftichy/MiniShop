namespace MiniShop.Contracts.Payments;

/// <summary>E-shop krok 8: brána potvrdila platbu objednávky (podepsaný webhook). Čte sága objednávky.</summary>
public sealed record PaymentSucceededV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    decimal Amount) : IIntegrationEvent;
