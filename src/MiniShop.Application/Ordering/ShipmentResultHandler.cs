using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Orders;

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
    IIntegrationMessageHandler<ShipmentCreatedV2>,
    IIntegrationMessageHandler<ShipmentFailedV2>,
    IIntegrationMessageHandler<ShipmentDeliveredV1>,
    IIntegrationMessageHandler<ShipmentReturnedV1>
{
    /// <summary>
    /// E-shop krok 26D: dopravce zásilku vrátil nedoručenou → objednávka ReturnedToSender (→ OrderReturnedToSenderV1: sklad,
    /// peníze, dobropis, e-mail a tržby si každý modul vyřídí sám). Už doručená / už vrácená = nic.
    /// E-shop krok 28C: zásilka balíku → <see cref="Order.ReturnParcelToSender"/> (jediný balík = celá objednávka jako dřív).
    /// </summary>
    public Task HandleAsync(ShipmentReturnedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ParcelByTrackingNumber(message.TrackingNumber) is not null
            ? order.ReturnParcelToSender(message.TrackingNumber, message.ReturnedAtUtc)
            : order.ReturnToSender(message.ReturnedAtUtc), cancellationToken);

    /// <summary>
    /// E-shop krok 26C: dopravce doručil → objednávka doručená (OrderCompleted → OrderCompletedV1, e-mail, faktura…).
    /// E-shop krok 28C: zásilka balíku → <see cref="Order.MarkParcelDelivered"/> – doručená je objednávka až s posledním balíkem.
    /// </summary>
    public Task HandleAsync(ShipmentDeliveredV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ParcelByTrackingNumber(message.TrackingNumber) is not null
            ? order.MarkParcelDelivered(message.TrackingNumber, message.DeliveredAtUtc)
            : order.MarkDeliveredByCarrier(message.DeliveredAtUtc), cancellationToken);

    /// <summary>
    /// Starší odpověď (předání celé objednávky z doby před balíky). Od e-shop kroku 28C posílá Shipping i V2 – objednávka,
    /// která předává po balících, V1 ignoruje (rozhodne V2 s číslem balíku).
    /// </summary>
    public Task HandleAsync(ShipmentCreatedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => !order.HasParcelShipments
            && order.ConfirmShipment(message.Carrier, message.TrackingNumber, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    public Task HandleAsync(ShipmentFailedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => !order.HasParcelShipments && order.FailShipment(message.Reason), cancellationToken);

    /// <summary>E-shop krok 28C: dopravce převzal balík → <see cref="Order.ConfirmParcelShipment"/> (poslední = objednávka odeslaná).</summary>
    public Task HandleAsync(ShipmentCreatedV2 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ConfirmParcelShipment(message.ParcelNumber, message.Carrier, message.TrackingNumber,
            timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    public Task HandleAsync(ShipmentFailedV2 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.FailParcelShipment(message.ParcelNumber, message.Reason), cancellationToken);

    /// <summary>Načíst, změnit, uložit jen když doména něco změnila (opožděná / opakovaná zpráva vrátí false).</summary>
    private async Task ChangeAsync(int orderId, Func<Order, bool> change, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        var order = await orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Objednávka {orderId} neexistuje.");

        if (change(order))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
