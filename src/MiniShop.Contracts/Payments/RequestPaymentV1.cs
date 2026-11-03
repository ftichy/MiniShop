namespace MiniShop.Contracts.Payments;

/// <summary>
/// E-shop krok 8: příkaz pro modul Payments – připrav platbu objednávky (kartou přes bránu). Posílá ho orchestrátor ságy
/// po rezervaci zboží. Payments odpoví až po výsledku z brány: <see cref="PaymentSucceededV1"/> nebo <see cref="PaymentFailedV1"/>.
/// </summary>
/// <param name="CustomerId">Komu platba patří – stránku brány smí otevřít jen on.</param>
/// <param name="ExpiresAtUtc">Do kdy jde zaplatit (termín ságy); pak ji sága zamítne a zboží vrátí.</param>
public sealed record RequestPaymentV1(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerId,
    decimal Amount,
    DateTime ExpiresAtUtc) : IIntegrationCommand;
