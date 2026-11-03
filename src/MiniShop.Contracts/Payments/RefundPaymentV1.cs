namespace MiniShop.Contracts.Payments;

/// <summary>
/// E-shop krok 8: příkaz pro Payments – vrať peníze (kompenzace). Posílá ho sága, když platba přišla pozdě –
/// objednávka už je zamítnutá a zboží zpátky na skladě. Payments odpoví <see cref="PaymentRefundedV1"/>.
/// </summary>
public sealed record RefundPaymentV1(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId) : IIntegrationCommand;
