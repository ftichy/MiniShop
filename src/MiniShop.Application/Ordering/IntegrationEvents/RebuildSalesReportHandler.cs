using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Reporting;

namespace MiniShop.Application.Ordering.IntegrationEvents;

/// <summary>
/// E-shop krok 11: vyřídí příkaz <see cref="RebuildSalesReportV1"/> – pošle aktuální stav každé objednávky jako
/// <see cref="OrderSnapshotV1"/>. Kdo si z událostí staví vlastní přehled (Reporting), postaví ho z nich znovu.
/// </summary>
/// <remarks>
/// Stejný vzor jako obnova katalogu ve skladu (SyncCatalogHandler). Ordering neví, kdo snímky čte – jen je oznámí.
/// Všechny jdou přes outbox v jedné transakci (u výukového e-shopu je objednávek málo).
/// </remarks>
public sealed class RebuildSalesReportHandler(
    IOrderRepository orders,
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IIntegrationMessageHandler<RebuildSalesReportV1>
{
    public async Task HandleAsync(RebuildSalesReportV1 command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var order in await orders.GetAllAsync(customerId: null, cancellationToken))
        {
            publisher.Publish(new OrderSnapshotV1(
                Guid.NewGuid(),
                now,
                order.Id,
                order.CreatedAtUtc,
                order.Status.ToString(),
                order.Items
                    .Select(i => new OrderItemV2(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.Discount, i.LineTotal))
                    .ToList(),
                order.ShippingPrice,
                order.TotalPrice));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
