using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Shipping;
using MiniShop.Domain.Shipping;

namespace MiniShop.Infrastructure.Shipping;

public sealed class ShipmentRepository(ShippingDbContext db) : IShipmentRepository
{
    private static readonly ShipmentStatus[] OnTheWay =
        [ShipmentStatus.Created, ShipmentStatus.PickedUp, ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery];

    public Task<Shipment?> GetByCommandIdAsync(Guid commandId, CancellationToken cancellationToken = default) =>
        db.Shipments.FirstOrDefaultAsync(s => s.CommandId == commandId, cancellationToken);

    public Task<Shipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default) =>
        db.Shipments.FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber, cancellationToken);

    public async Task<IReadOnlyList<Shipment>> GetQuietAsync(DateTime quietSinceUtc, int batchSize,
        CancellationToken cancellationToken = default) =>
        await db.Shipments
            .Where(s => OnTheWay.Contains(s.Status)
                && (s.LastCarrierUpdateAtUtc ?? s.RequestedAtUtc) < quietSinceUtc
                && (s.LastCheckedAtUtc == null || s.LastCheckedAtUtc < quietSinceUtc))
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
