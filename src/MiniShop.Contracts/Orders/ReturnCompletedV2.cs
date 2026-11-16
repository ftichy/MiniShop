namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 22: vratka po částech je hotová – vrácené kusy jsou zpět na skladu, kartou peníze vrátil Payments
/// (<c>PaymentRefundedV2</c>), u dobírky je posílá admin převodem. Objednávka zůstává doručená.
/// Vratku celé objednávky najednou dál oznamuje <see cref="ReturnCompletedV1"/>.
/// </summary>
/// <param name="ReturnNumber">Číslo vratky v objednávce (1, 2, …).</param>
/// <param name="Items">Vrácené položky a kusy.</param>
/// <param name="ShippingRefund">Vrácená doprava (dnes vždy 0 – dopravu vrací jen vratka celé objednávky).</param>
/// <param name="Amount">Kolik se vrací celkem (položky + doprava).</param>
/// <param name="RefundedByCard">true = vráceno na kartu; false = dobírka, peníze pošle admin ručně.</param>
/// <param name="FullyReturnedProductIds">Produkty, ze kterých si zákazník po této vratce nenechal ani kus.</param>
public sealed record ReturnCompletedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ReturnNumber,
    string CustomerEmail,
    IReadOnlyList<ReturnItemV1> Items,
    decimal ShippingRefund,
    decimal Amount,
    bool RefundedByCard,
    IReadOnlyList<int> FullyReturnedProductIds) : IIntegrationEvent;
