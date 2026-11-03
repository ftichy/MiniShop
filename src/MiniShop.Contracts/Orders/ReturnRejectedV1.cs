namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 12: vrácení neprošlo – admin ho zamítl, nebo zboží nedorazilo včas. Objednávka zůstává doručená.
/// </summary>
public sealed record ReturnRejectedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail,
    string Reason) : IIntegrationEvent;
