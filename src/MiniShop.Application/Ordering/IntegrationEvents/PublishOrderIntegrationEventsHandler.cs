using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Contracts.Orders;
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
public sealed class PublishOrderIntegrationEventsHandler(IOrderingMessagePublisher publisher, IOrderingUnitOfWork unitOfWork) :
    IDomainEventHandler<OrderPlaced>,
    IDomainEventHandler<OrderConfirmed>,
    IDomainEventHandler<OrderRejected>,
    IDomainEventHandler<OrderShipped>,
    IDomainEventHandler<OrderCompleted>,
    IDomainEventHandler<OrderCancelled>
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

    private async Task PublishAsync(CancellationToken cancellationToken, params IIntegrationEvent[] integrationEvents)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            publisher.Publish(integrationEvent);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<OrderLineV1> ToLines(Order order) =>
        order.Items.Select(i => new OrderLineV1(i.ProductId, i.Quantity)).ToList();
}
