using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.IntegrationEvents;

/// <summary>
/// E-shop krok 28B (směr C): sklad oznámil, odkud zboží objednávky vzal (<see cref="StockReservedV2"/>) – Ordering si to
/// uloží jako balíky objednávky (<see cref="Order.SetParcels"/>). Sága na tuhle zprávu nečeká – potvrzení řídí dál V1.
/// </summary>
/// <remarks>
/// Opakovaná zpráva nic nezmění (balíky jen jednou). Neznámá objednávka (nemělo by nastat) se přeskočí.
/// Zpráva může přijít až po potvrzení – balíky nejsou podmínkou ničeho, jen popisem.
/// </remarks>
public sealed class OrderParcelsHandler(IOrderRepository orders, IOrderingUnitOfWork unitOfWork)
    : IIntegrationMessageHandler<StockReservedV2>
{
    public async Task HandleAsync(StockReservedV2 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        if (await orders.GetByIdAsync(message.OrderId, cancellationToken) is not { } order)
        {
            return;
        }

        var parcels = message.Parcels
            .Select(p => new ParcelPlan(p.WarehouseCode, p.WarehouseName, [.. p.Lines.Select(l => new OrderParcelLine(l.ProductId, l.Quantity))]))
            .ToList();
        if (order.SetParcels(parcels))
        {
            TraceTags.Tag("minishop.order.parcels", parcels.Count);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
