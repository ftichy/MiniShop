using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Shipping;
using MiniShop.Domain.Shipping;

namespace MiniShop.Infrastructure.Shipping;

public sealed class ShipmentRepository(ShippingDbContext db) : IShipmentRepository
{
    public Task<Shipment?> GetByCommandIdAsync(Guid commandId, CancellationToken cancellationToken = default) =>
        db.Shipments.FirstOrDefaultAsync(s => s.CommandId == commandId, cancellationToken);

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);
}
