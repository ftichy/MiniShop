namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 26D: dopravce zásilku nedoručil a vrátil ji e-shopu. Událost (choreografie) – každý modul si udělá svoje:
/// sklad naskladní vydané zboží podle <paramref name="Lines"/>, Payments u karty vrátí peníze, Invoicing vystaví dobropis
/// (je-li faktura), Notifications napíše zákazníkovi, Reporting objednávku nepočítá do tržby.
/// </summary>
/// <param name="TotalPrice">Celá cena objednávky (položky + doprava) – kolik se vrací, když bylo zaplaceno kartou.</param>
/// <param name="RefundedByCard">true = placeno kartou, Payments vrátí peníze; false = dobírka (nezaplaceno, nic se nevrací).</param>
public sealed record OrderReturnedToSenderV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail,
    decimal TotalPrice,
    bool RefundedByCard,
    IReadOnlyList<OrderLineV1> Lines) : IIntegrationEvent;
