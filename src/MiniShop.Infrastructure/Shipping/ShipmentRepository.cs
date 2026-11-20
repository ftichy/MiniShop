using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Shipping;
using MiniShop.Domain.Shipping;

namespace MiniShop.Infrastructure.Shipping;

public sealed class ShipmentRepository(ShippingDbContext db) : IShipmentRepository
{
    private static readonly ShipmentStatus[] OnTheWay = Shipment.OnTheWayStatuses;
    private static readonly ShipmentStatus[] Finished = Shipment.FinishedStatuses;

    public Task<Shipment?> GetByCommandIdAsync(Guid commandId, CancellationToken cancellationToken = default) =>
        db.Shipments.FirstOrDefaultAsync(s => s.CommandId == commandId, cancellationToken);

    public Task<Shipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default) =>
        db.Shipments.FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber, cancellationToken);

    public async Task<IReadOnlyList<Shipment>> GetQuietAsync(DateTime quietSinceUtc, int batchSize,
        CancellationToken cancellationToken = default) =>
        await db.Shipments
            .Where(s => (OnTheWay.Contains(s.Status)
                    && (s.LastCarrierUpdateAtUtc ?? s.RequestedAtUtc) < quietSinceUtc
                    && (s.LastCheckedAtUtc == null || s.LastCheckedAtUtc < quietSinceUtc))
                // E-shop krok 26E: ukončená s dírou v historii (ztracený webhook) – jednou po konci: další novinka
                // (doplněný stav) posune LastCarrierUpdateAtUtc, ale dohledání ho hned dorovná LastCheckedAtUtc.
                || (Finished.Contains(s.Status)
                    && s.Tracking.Count < s.LastSequence
                    && s.LastCarrierUpdateAtUtc < quietSinceUtc
                    && (s.LastCheckedAtUtc == null || s.LastCheckedAtUtc < s.LastCarrierUpdateAtUtc)))
            .OrderBy(s => s.LastCarrierUpdateAtUtc ?? s.RequestedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Shipment>> GetCreatedByOrderIdsAsync(IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken = default) =>
        await db.Shipments.AsNoTracking()
            .Where(s => orderIds.Contains(s.OrderId) && s.Status != ShipmentStatus.Failed)
            .ToListAsync(cancellationToken);

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);
}
