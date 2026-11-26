using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Orders;

namespace MiniShop.Application.Inventory;

/// <summary>
/// E-shop krok 6: objednávka odešla (<see cref="OrderShippedV1"/>) → rezervace objednávky se mění na výdej.
/// Zásoba se nemění (odečetla se už při rezervaci), jen sklad ví, že zboží opravdu odešlo a nevrátí se.
/// </summary>
/// <remarks>
/// Choreografie místo orchestrace: sága skladu nic neposílá a na odpověď nečeká – výdej nemůže selhat jako rezervace,
/// stačí oznámení. Opakované doručení zahodí inbox; rezervace už vydané (nebo uvolněné) se přeskočí.
/// </remarks>
public sealed class FulfilReservationHandler(
    IStockReservationRepository reservations,
    IProductRepository products,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<OrderShippedV1>,
    IIntegrationMessageHandler<ParcelShippedV1>
{
    /// <summary>
    /// E-shop krok 28D: odešel jeden balík rozdělené objednávky → vydat rezervace jeho skladu hned (ne až s posledním
    /// balíkem). Vrácený balík se pak dá naskladnit (vratka bere jen vydané rezervace), i když druhý balík ještě čeká.
    /// Sklad pozná podle názvu ve zprávě; neznámý název = nic (vydá se s OrderShippedV1).
    /// </summary>
    public async Task HandleAsync(ParcelShippedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        var warehouse = (await products.GetWarehousesAsync(cancellationToken)).FirstOrDefault(w => w.Name == message.WarehouseName);
        if (warehouse is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var reservation in (await reservations.GetActiveByOrderIdAsync(message.OrderId, cancellationToken))
                     .Where(r => r.WarehouseId == warehouse.Id))
        {
            reservation.Fulfil(now);
        }

        TraceTags.Tag("minishop.stock.result", "fulfilled");
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(OrderShippedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);   // span "process OrderShippedV1" ve skladu
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var reservation in await reservations.GetActiveByOrderIdAsync(message.OrderId, cancellationToken))
        {
            reservation.Fulfil(now);
        }

        TraceTags.Tag("minishop.stock.result", "fulfilled");   // zboží odešlo – rezervace vydané

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
