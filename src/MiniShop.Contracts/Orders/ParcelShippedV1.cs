namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 28C (směr C): jeden balík objednávky je na cestě. Objednávka jako celek je odeslaná až s posledním balíkem
/// (<see cref="OrderShippedV1"/>, beze změny). Notifications podle této zprávy píše „balík 1 ze 2 je na cestě“.
/// </summary>
/// <remarks>Nese e-mail zákazníka (osobní údaj) – kvůli e-mailu bez dalšího dotazu, jako <see cref="OrderShippedV1"/>.</remarks>
/// <param name="ParcelCount">Kolik balíků objednávka má – u jednoho balíku stačí e-mail o odeslání objednávky.</param>
public sealed record ParcelShippedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ParcelNumber,
    int ParcelCount,
    string WarehouseName,
    string CustomerEmail,
    string Carrier,
    string TrackingNumber) : IIntegrationEvent;
