using MiniShop.Domain.Shipping;

namespace MiniShop.Application.Shipping;

/// <summary>
/// E-shop krok 26C: průběh zásilek pro stránky (detail objednávky, Expedice). Web skládá obrazovku z modulů – Ordering
/// o stavu zásilky neví nic kromě „odesláno“ a „doručeno“.
/// </summary>
public sealed class ShipmentQueryService(IShipmentRepository shipments)
{
    public async Task<IReadOnlyDictionary<int, ShipmentTrackingDto>> GetByOrderIdsAsync(IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<int, ShipmentTrackingDto>();
        }

        // E-shop krok 28C: objednávka může mít víc zásilek (balíky) – tady první balík (Expedice, starší stránky).
        var found = await shipments.GetCreatedByOrderIdsAsync(orderIds, cancellationToken);
        return found.GroupBy(s => s.OrderId).ToDictionary(g => g.Key, g => ToDto(g.OrderBy(s => s.ParcelNumber).First()));
    }

    /// <summary>E-shop krok 28C: všechny zásilky objednávky (jedna za každý balík) podle čísla balíku – detail objednávky.</summary>
    public async Task<IReadOnlyList<ShipmentTrackingDto>> GetAllByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        [.. (await shipments.GetCreatedByOrderIdsAsync([orderId], cancellationToken)).OrderBy(s => s.ParcelNumber).Select(ToDto)];

    public async Task<ShipmentTrackingDto?> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        (await GetByOrderIdsAsync([orderId], cancellationToken)).GetValueOrDefault(orderId);

    private static ShipmentTrackingDto ToDto(Shipment s) => new(
        s.TrackingNumber ?? "",
        s.Carrier ?? "",
        s.Status.ToString(),
        s.LastCarrierUpdateAtUtc,
        s.UnknownAtCarrier,
        s.Tracking.OrderBy(t => t.Sequence).Select(t => new ShipmentTrackingStepDto(t.Status.ToString(), t.OccurredAtUtc)).ToList(),
        s.ParcelNumber);
}

/// <summary>
/// E-shop krok 26C: zásilka u dopravce pro stránky. <paramref name="Status"/> = <see cref="ShipmentStatus"/> jako text
/// (<c>Created</c>, <c>PickedUp</c>, <c>InTransit</c>, <c>OutForDelivery</c>, <c>Delivered</c>); české popisky dává Web.
/// </summary>
public sealed record ShipmentTrackingDto(
    string TrackingNumber,
    string Carrier,
    string Status,
    DateTime? LastUpdateAtUtc,
    bool UnknownAtCarrier,
    IReadOnlyList<ShipmentTrackingStepDto> Steps,
    int ParcelNumber = 1);

public sealed record ShipmentTrackingStepDto(string Status, DateTime OccurredAtUtc);
