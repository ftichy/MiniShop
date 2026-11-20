using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Shipping;

namespace MiniShop.Application.Ordering;

/// <summary>
/// E-shop krok 26B: odpověď modulu Shipping. Přijato → objednávka odešla (OrderShipped → OrderShippedV1: e-mail, sklad
/// vydá rezervaci); odmítnuto → zpět do „K odeslání“ s hláškou pro skladníka.
/// </summary>
/// <remarks>Opožděná/opakovaná zpráva (objednávka už odeslaná, mezitím ručně) nic nezmění – doména vrátí false.</remarks>
public sealed class ShipmentResultHandler(
    IOrderRepository orders,
    IOrderingUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<ShipmentCreatedV1>,
    IIntegrationMessageHandler<ShipmentFailedV1>,
    IIntegrationMessageHandler<ShipmentDeliveredV1>
{
    /// <summary>E-shop krok 26C: dopravce doručil → objednávka doručená (OrderCompleted → OrderCompletedV1, e-mail, faktura…).</summary>
    public async Task HandleAsync(ShipmentDeliveredV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        var order = await orders.GetByIdAsync(message.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Objednávka {message.OrderId} neexistuje.");

        if (order.MarkDeliveredByCarrier(message.DeliveredAtUtc))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task HandleAsync(ShipmentCreatedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        var order = await orders.GetByIdAsync(message.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Objednávka {message.OrderId} neexistuje.");

        if (order.ConfirmShipment(message.Carrier, message.TrackingNumber, timeProvider.GetUtcNow().UtcDateTime))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task HandleAsync(ShipmentFailedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        var order = await orders.GetByIdAsync(message.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Objednávka {message.OrderId} neexistuje.");

        if (order.FailShipment(message.Reason))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
