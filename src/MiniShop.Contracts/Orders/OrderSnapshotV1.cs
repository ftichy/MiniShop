namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 11: aktuální stav jedné objednávky jako odpověď na <see cref="Reporting.RebuildSalesReportV1"/> –
/// podklad pro přestavění čtecího modelu (projekce). Nejde o nový fakt („objednávka vznikla“), proto vlastní kontrakt:
/// kdyby Ordering poslal znovu <see cref="OrderPlacedV2"/>, Notifications by rozeslaly e-maily o starých objednávkách.
/// </summary>
/// <remarks>Bez osobních údajů – jméno, e-mail ani adresu přehled nepotřebuje.</remarks>
/// <param name="Status">Název stavu objednávky (<c>Pending</c>, <c>Confirmed</c>, <c>Cancelled</c>…).</param>
public sealed record OrderSnapshotV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    DateTime PlacedAtUtc,
    string Status,
    IReadOnlyList<OrderItemV2> Items,
    decimal ShippingPrice,
    decimal TotalPrice) : IIntegrationEvent;
