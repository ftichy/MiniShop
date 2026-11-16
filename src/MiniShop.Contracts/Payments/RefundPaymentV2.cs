namespace MiniShop.Contracts.Payments;

/// <summary>
/// E-shop krok 22: příkaz pro Payments – vrať <b>část</b> peněz za jednu vratku. <see cref="RefundPaymentV1"/> vrací
/// celou platbu (pozdní platba, zrušení, vrácení celé objednávky). Payments odpoví <see cref="PaymentRefundedV2"/>.
/// </summary>
/// <param name="ReturnNumber">Pořadí vratky v objednávce – stejná vratka podruhé peníze nevrátí.</param>
/// <param name="Amount">Kolik vrátit (Kč); součet všech vrácení nesmí přesáhnout zaplacenou částku.</param>
public sealed record RefundPaymentV2(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ReturnNumber,
    decimal Amount) : IIntegrationCommand;
