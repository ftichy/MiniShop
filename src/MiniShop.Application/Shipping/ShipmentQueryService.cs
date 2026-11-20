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

        var found = await shipments.GetCreatedByOrderIdsAsync(orderIds, cancellationToken);
        return found.ToDictionary(s => s.OrderId, ToDto);
    }

    public async Task<ShipmentTrackingDto?> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        (await GetByOrderIdsAsync([orderId], cancellationToken)).GetValueOrDefault(orderId);

    private static ShipmentTrackingDto ToDto(Shipment s) => new(
        s.TrackingNumber ?? "",
        s.Carrier ?? "",
        s.Status.ToString(),
        s.LastCarrierUpdateAtUtc,
        s.UnknownAtCarrier,
        s.Tracking.OrderBy(t => t.Sequence).Select(t => new ShipmentTrackingStepDto(t.Status.ToString(), t.OccurredAtUtc)).ToList());
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
    IReadOnlyList<ShipmentTrackingStepDto> Steps);

public sealed record ShipmentTrackingStepDto(string Status, DateTime OccurredAtUtc);
