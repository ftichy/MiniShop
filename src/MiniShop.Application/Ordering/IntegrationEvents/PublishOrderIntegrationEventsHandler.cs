using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Shipping;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Ordering.IntegrationEvents;

/// <summary>
/// Překladač (krok 7): z interních doménových událostí objednávky vyrobí veřejné integrační události (…V1, …V2).
/// Jediné místo, kde se rozhoduje, co se o objednávce dozví okolní svět.
/// </summary>
/// <remarks>
/// Když se změní entita Order, upraví se jen tenhle překlad – kontrakty a jejich příjemci zůstanou stejné.
/// Běží uvnitř dekorátoru IdempotentDomainEventHandler (krok 6): zápis do outboxu se potvrdí spolu s odškrtnutím
/// v inboxu, takže se při opakovaném doručení doménové události integrační událost nepublikuje dvakrát.
/// E-shop krok 7: Ordering už zákazníkovi nepíše – jen oznamuje fakta, e-maily si podle nich posílá modul Notifications.
/// </remarks>
public sealed class PublishOrderIntegrationEventsHandler(
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    ReturnSettings returnSettings) :
    IDomainEventHandler<OrderPlaced>,
    IDomainEventHandler<OrderConfirmed>,
    IDomainEventHandler<OrderRejected>,
    IDomainEventHandler<OrderShipped>,
    IDomainEventHandler<OrderCompleted>,
    IDomainEventHandler<OrderCancelled>,
    IDomainEventHandler<OrderReturnRequested>,
    IDomainEventHandler<OrderReturnRejected>,
    IDomainEventHandler<OrderReturned>,
    IDomainEventHandler<OrderPartialReturnRequested>,
    IDomainEventHandler<OrderPartialReturnRejected>,
    IDomainEventHandler<OrderPartialReturnCompleted>,
    IDomainEventHandler<OrderShipmentRequested>,
    IDomainEventHandler<OrderParcelShipmentRequested>,   // e-shop krok 28C
    IDomainEventHandler<OrderParcelShipped>,
    IDomainEventHandler<OrderReturnedToSender>,
    IDomainEventHandler<OrderStatusChanged>
{
    /// <summary>
    /// Vznik objednávky ve dvou verzích (e-shop krok 7, expand → contract): V1 pro stávající odběratele,
    /// V2 s celým snímkem (jméno, položky s názvy, doprava, adresa) pro Notifications.
    /// </summary>
    public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        var order = domainEvent.Order;
        return PublishAsync(cancellationToken,
            new OrderPlacedV1(Guid.NewGuid(), domainEvent.OccurredAtUtc, order.Id, order.CustomerEmail, order.TotalPrice, ToLines(order)),
            new OrderPlacedV2(
                Guid.NewGuid(),
                domainEvent.OccurredAtUtc,
                order.Id,
                order.CustomerId,
                order.CustomerName,
                order.CustomerEmail,
                order.Items
                    .Select(i => new OrderItemV2(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.Discount, i.LineTotal))
                    .ToList(),
                order.ShippingMethod.ToString(),
                order.ShippingPrice,
                order.DeliveryAddress?.ToString(),
                order.TotalPrice));
    }

    /// <summary>E-shop krok 7: dřív jen interní událost (e-mail posílal Ordering) – teď fakt pro Notifications.</summary>
    public Task HandleAsync(OrderConfirmed domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new OrderConfirmedV1(Guid.NewGuid(), domainEvent.OccurredAtUtc, domainEvent.Order.Id));

    public Task HandleAsync(OrderRejected domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new OrderRejectedV1(
            Guid.NewGuid(), domainEvent.OccurredAtUtc, domainEvent.Order.Id, domainEvent.Order.RejectionReason ?? string.Empty));

    /// <summary>E-shop krok 6: odeslání je fakt pro okolí – sklad podle něj vydá rezervaci, notifikace pošlou číslo zásilky.</summary>
    public Task HandleAsync(OrderShipped domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new OrderShippedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            domainEvent.Order.Carrier,
            domainEvent.Order.TrackingNumber));

    public Task HandleAsync(OrderCompleted domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new OrderCompletedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail));

    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new OrderCancelledV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            ToLines(domainEvent.Order)));

    /// <summary>E-shop krok 12: zákazník chce vrátit – fakt pro Notifications (s termínem, do kdy poslat zboží).</summary>
    public Task HandleAsync(OrderReturnRequested domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new ReturnRequestedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            domainEvent.Order.ReturnReason ?? string.Empty,
            domainEvent.OccurredAtUtc + returnSettings.SendWindow));   // stejný termín jako v ReturnSaga

    public Task HandleAsync(OrderReturnRejected domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new ReturnRejectedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            domainEvent.Order.ReturnRejectionReason ?? string.Empty));

    /// <summary>E-shop krok 12: vráceno – Notifications napíše zákazníkovi, Reporting odečte z tržby.</summary>
    public Task HandleAsync(OrderReturned domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new ReturnCompletedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            domainEvent.Order.TotalPrice,
            RefundedByCard: domainEvent.Order.PaymentMethod == PaymentMethod.Card));

    /// <summary>E-shop krok 22: vratka po částech – které kusy poslat a do kdy (Notifications).</summary>
    public Task HandleAsync(OrderPartialReturnRequested domainEvent, CancellationToken cancellationToken)
    {
        var orderReturn = domainEvent.Order.GetReturn(domainEvent.ReturnNumber);
        return PublishAsync(cancellationToken, new ReturnRequestedV2(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.ReturnNumber,
            domainEvent.Order.CustomerEmail,
            orderReturn.Reason,
            orderReturn.RequestedAtUtc + returnSettings.SendWindow,   // stejný termín jako v ReturnSaga
            ToReturnItems(orderReturn)));
    }

    public Task HandleAsync(OrderPartialReturnRejected domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new ReturnRejectedV2(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.ReturnNumber,
            domainEvent.Order.CustomerEmail,
            domainEvent.Order.GetReturn(domainEvent.ReturnNumber).RejectionReason ?? string.Empty));

    /// <summary>
    /// E-shop krok 22: vratka po částech hotová – dobropis jen za vrácené (Invoicing), e-mail, tržba bez vrácených (Reporting),
    /// hodnocení produktů vrácených celých už ne (Reviews). Doprava se u vratky po částech nevrací.
    /// </summary>
    public Task HandleAsync(OrderPartialReturnCompleted domainEvent, CancellationToken cancellationToken)
    {
        var order = domainEvent.Order;
        var orderReturn = order.GetReturn(domainEvent.ReturnNumber);
        return PublishAsync(cancellationToken, new ReturnCompletedV2(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            order.Id,
            domainEvent.ReturnNumber,
            order.CustomerEmail,
            ToReturnItems(orderReturn),
            ShippingRefund: 0m,
            orderReturn.Amount,
            RefundedByCard: order.PaymentMethod == PaymentMethod.Card,
            order.FullyReturnedProductIds()));
    }

    /// <summary>
    /// E-shop krok 26B: předat dopravci = PŘÍKAZ modulu Shipping (jeden příjemce). Adresa a jméno odchází dál dopravci;
    /// dobírka = celá částka objednávky. ID příkazu bude u dopravce Idempotency-Key.
    /// </summary>
    public async Task HandleAsync(OrderShipmentRequested domainEvent, CancellationToken cancellationToken)
    {
        var order = domainEvent.Order;
        var address = order.DeliveryAddress
            ?? throw new InvalidOperationException($"Objednávka {order.Id} předaná dopravci nemá adresu.");

        publisher.Send(new CreateShipmentV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            order.Id,
            order.CustomerName,
            address.Street,
            address.City,
            address.PostalCode,
            order.PaymentMethod == PaymentMethod.CashOnDelivery ? order.TotalPrice : null));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// E-shop krok 28C: předat dopravci jeden balík = příkaz <see cref="CreateShipmentV2"/>. Dobírka po balících: každý
    /// balík vybere hodnotu svých položek (po slevě), první balík navíc dopravu – dohromady celá objednávka.
    /// </summary>
    public async Task HandleAsync(OrderParcelShipmentRequested domainEvent, CancellationToken cancellationToken)
    {
        var order = domainEvent.Order;
        var address = order.DeliveryAddress
            ?? throw new InvalidOperationException($"Objednávka {order.Id} předaná dopravci nemá adresu.");
        var parcel = order.Parcels.First(p => p.Number == domainEvent.ParcelNumber);

        decimal? cashOnDelivery = null;
        if (order.PaymentMethod == PaymentMethod.CashOnDelivery)
        {
            cashOnDelivery = parcel.Lines.Sum(l => LineValue(order, l.ProductId, l.Quantity))
                + (parcel.Number == order.Parcels.Min(p => p.Number) ? order.ShippingPrice : 0m);
        }

        TraceTags.Tag("minishop.shipment.parcel", parcel.Number);
        publisher.Send(new CreateShipmentV2(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            order.Id,
            parcel.Number,
            parcel.WarehouseName,
            order.CustomerName,
            address.Street,
            address.City,
            address.PostalCode,
            cashOnDelivery));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>E-shop krok 28C: balík je na cestě – <see cref="ParcelShippedV1"/> (e-mail „balík 1 ze 2“).</summary>
    public Task HandleAsync(OrderParcelShipped domainEvent, CancellationToken cancellationToken)
    {
        var order = domainEvent.Order;
        var parcel = order.Parcels.First(p => p.Number == domainEvent.ParcelNumber);
        return PublishAsync(cancellationToken, new ParcelShippedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            order.Id,
            parcel.Number,
            order.Parcels.Count,
            parcel.WarehouseName,
            order.CustomerEmail,
            parcel.Carrier ?? string.Empty,
            parcel.TrackingNumber ?? string.Empty));
    }

    /// <summary>Hodnota kusů produktu po slevě (sleva je rozpočítaná do položek – e-shop krok 13).</summary>
    private static decimal LineValue(Order order, int productId, int quantity)
    {
        var items = order.Items.Where(i => i.ProductId == productId).ToList();
        var pieces = items.Sum(i => i.Quantity);
        return pieces == 0 ? 0m : Math.Round(items.Sum(i => i.LineTotal) * quantity / pieces, 2);
    }

    /// <summary>
    /// E-shop krok 26D: zásilka se vrátila nedoručená – UDÁLOST (choreografie), žádný příkaz: Ordering neví, kdo poslouchá.
    /// Peníze vrací Payments jen u karty (dobírka nebyla zaplacena), zboží sklad podle položek.
    /// </summary>
    public Task HandleAsync(OrderReturnedToSender domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new OrderReturnedToSenderV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            domainEvent.Order.TotalPrice,
            RefundedByCard: domainEvent.Order.PaymentMethod == PaymentMethod.Card,
            ToLines(domainEvent.Order)));

    /// <summary>
    /// Vzhled krok F3a: každá změna stavu = obecná událost (signál otevřeným stránkám). Stav z načtené objednávky – u víc
    /// změn v jedné transakci přijde několikrát týž (aktuální) stav; pro signál „obnov se“ to nevadí.
    /// </summary>
    public Task HandleAsync(OrderStatusChanged domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(cancellationToken, new OrderStatusChangedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerId,
            domainEvent.Order.Status.ToString()));

    private async Task PublishAsync(CancellationToken cancellationToken, params IIntegrationEvent[] integrationEvents)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            publisher.Publish(integrationEvent);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<ReturnItemV1> ToReturnItems(OrderReturn orderReturn) =>
        orderReturn.Lines.Select(l => new ReturnItemV1(l.ProductId, l.ProductName, l.Quantity, l.UnitPrice, l.Discount, l.LineTotal)).ToList();

    private static IReadOnlyList<OrderLineV1> ToLines(Order order) =>
        order.Items.Select(i => new OrderLineV1(i.ProductId, i.Quantity)).ToList();
}
