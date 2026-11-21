namespace MiniShop.Contracts.Orders;

/// <summary>
/// Vzhled krok F3a: stav objednávky se změnil – obecná událost pro <b>každý</b> přechod (i ty, které vlastní událost nemají:
/// čeká na platbu, balí se). Web podle ní pošle otevřeným stránkám signál „obnov se“ (SignalR); nese jen ID, žádné osobní údaje.
/// </summary>
/// <param name="CustomerId">ID účtu zákazníka (null = objednávka z API nebo anonymizovaná) – signál pro jeho seznam objednávek.</param>
/// <param name="Status">Nový stav jako text (<c>OrderStatus</c> v Orderingu, např. Confirmed, Shipped).</param>
public sealed record OrderStatusChangedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string? CustomerId,
    string Status) : IIntegrationEvent;
