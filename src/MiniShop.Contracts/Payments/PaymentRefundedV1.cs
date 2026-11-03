namespace MiniShop.Contracts.Payments;

/// <summary>E-shop krok 8: peníze za objednávku se vracejí zákazníkovi (pozdní platba). Čtou notifikace.</summary>
public sealed record PaymentRefundedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    decimal Amount) : IIntegrationEvent;
