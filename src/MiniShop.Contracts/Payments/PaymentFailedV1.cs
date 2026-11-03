namespace MiniShop.Contracts.Payments;

/// <summary>E-shop krok 8: brána platbu zamítla (zákazník zrušil, banka odmítla). Sága objednávku zamítne a vrátí zboží.</summary>
public sealed record PaymentFailedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string Reason) : IIntegrationEvent;
