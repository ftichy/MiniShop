namespace MiniShop.Contracts.Payments;

/// <summary>
/// E-shop krok 22: Payments vrátil část peněz za vratku (odpověď na <see cref="RefundPaymentV2"/>).
/// Celé vrácení dál oznamuje <see cref="PaymentRefundedV1"/>.
/// </summary>
/// <param name="Amount">Vrácená částka této vratky.</param>
/// <param name="TotalRefunded">Kolik je z platby vráceno celkem (všechny vratky).</param>
public sealed record PaymentRefundedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ReturnNumber,
    decimal Amount,
    decimal TotalRefunded) : IIntegrationEvent;
