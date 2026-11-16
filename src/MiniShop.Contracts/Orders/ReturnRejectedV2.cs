namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 22: vratka po částech neprošla (admin ji zamítl, nebo zboží nedorazilo včas). Zamítá se celá vratka.
/// </summary>
public sealed record ReturnRejectedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ReturnNumber,
    string CustomerEmail,
    string Reason) : IIntegrationEvent;
