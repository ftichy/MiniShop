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
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IIntegrationMessageHandler<OrderShippedV1>
{
    public async Task HandleAsync(OrderShippedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);   // span "process OrderShippedV1" ve skladu
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var reservation in await reservations.GetActiveByOrderIdAsync(message.OrderId, cancellationToken))
        {
            reservation.Fulfil(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
