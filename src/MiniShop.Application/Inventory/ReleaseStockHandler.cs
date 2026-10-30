using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Modul Inventory (krok 9): vyřídí příkaz <see cref="ReleaseStockV1"/> – kompenzace. Vrátí zarezervované zboží na sklad
/// a odpoví <see cref="StockReleasedV1"/>. Když žádná rezervace není, odpoví také (není co vracet).
/// </summary>
/// <remarks>Nahrazuje ReturnStockOnOrderCancelledHandler z kroku 3 – sklad teď vrací podle svých rezervací, ne podle objednávky.</remarks>
public sealed class ReleaseStockHandler(
    IProductRepository products,
    IStockReservationRepository reservations,
    IInventoryMessagePublisher publisher,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IIntegrationMessageHandler<ReleaseStockV1>
{
    public async Task HandleAsync(ReleaseStockV1 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);   // span "process ReleaseStockV1" ve skladu
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var active = await reservations.GetActiveByOrderIdAsync(command.OrderId, cancellationToken);

        // Produkt mezitím mohl být smazán – rezervaci pak jen uvolníme.
        var productsById = (await products.GetByIdsAsync(active.Select(r => r.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        foreach (var reservation in active)
        {
            if (productsById.TryGetValue(reservation.ProductId, out var product))
            {
                product.AddStock(reservation.Quantity, now);
            }

            reservation.Release(now);
        }

        publisher.Publish(new StockReleasedV1(Guid.NewGuid(), now, command.OrderId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
