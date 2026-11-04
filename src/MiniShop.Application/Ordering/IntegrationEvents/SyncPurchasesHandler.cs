using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Reviews;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.IntegrationEvents;

/// <summary>
/// E-shop krok 14: vyřídí příkaz <see cref="SyncPurchasesV1"/> – pošle snímek každé objednávky i se zákazníkem
/// (<see cref="OrderSnapshotV2"/>). Nový odběratel (Reviews) si z nich doplní nákupy z doby před sebou.
/// </summary>
/// <remarks>
/// Stejný vzor jako <see cref="RebuildSalesReportHandler"/>. Anonymizované objednávky (smazaný účet) se neposílají –
/// zákazník už neexistuje a jeho osobní údaje se nesmí znovu rozeslat.
/// </remarks>
public sealed class SyncPurchasesHandler(
    IOrderRepository orders,
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IIntegrationMessageHandler<SyncPurchasesV1>
{
    public async Task HandleAsync(SyncPurchasesV1 command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var order in await orders.GetAllAsync(customerId: null, cancellationToken))
        {
            if (order.CustomerName == Order.AnonymizedCustomerName)
            {
                continue;
            }

            publisher.Publish(new OrderSnapshotV2(
                Guid.NewGuid(),
                now,
                order.Id,
                order.CustomerId,
                order.CustomerName,
                order.CreatedAtUtc,
                order.Status.ToString(),
                order.Items
                    .Select(i => new OrderItemV2(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.Discount, i.LineTotal))
                    .ToList()));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
