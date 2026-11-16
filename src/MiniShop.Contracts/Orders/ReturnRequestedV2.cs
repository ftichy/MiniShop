namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 22: zákazník chce vrátit vybrané položky – jedna z (možná několika) vratek objednávky.
/// Vratku celé objednávky najednou dál oznamuje <see cref="ReturnRequestedV1"/>.
/// </summary>
/// <param name="ReturnNumber">Číslo vratky v objednávce (1, 2, …).</param>
/// <param name="SendByUtc">Do kdy musí zboží dorazit, jinak vratka vyprší.</param>
public sealed record ReturnRequestedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ReturnNumber,
    string CustomerEmail,
    string Reason,
    DateTime SendByUtc,
    IReadOnlyList<ReturnItemV1> Items) : IIntegrationEvent;
