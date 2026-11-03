namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 12: objednávka je vrácená – zboží zpět na skladu. Kartou peníze vrátil modul Payments (PaymentRefundedV1),
/// u dobírky je vrací admin ručně (převodem).
/// </summary>
/// <param name="Amount">Kolik se zákazníkovi vrací (celá objednávka včetně dopravy).</param>
/// <param name="RefundedByCard">true = vráceno na kartu; false = dobírka, peníze pošle admin ručně.</param>
public sealed record ReturnCompletedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail,
    decimal Amount,
    bool RefundedByCard) : IIntegrationEvent;
