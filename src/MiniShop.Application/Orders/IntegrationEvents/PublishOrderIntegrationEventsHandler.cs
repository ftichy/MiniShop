using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Orders.IntegrationEvents;

/// <summary>
/// Překladač (krok 7): z interních doménových událostí objednávky vyrobí veřejné integrační události (…V1).
/// Jediné místo, kde se rozhoduje, co se o objednávce dozví okolní svět.
/// </summary>
/// <remarks>
/// Když se změní entita Order, upraví se jen tenhle překlad – kontrakty a jejich příjemci zůstanou stejné.
/// Běží uvnitř dekorátoru IdempotentDomainEventHandler (krok 6): zápis do outboxu se potvrdí spolu s odškrtnutím
/// v inboxu, takže se při opakovaném doručení doménové události integrační událost nepublikuje dvakrát.
/// </remarks>
public sealed class PublishOrderIntegrationEventsHandler(IIntegrationEventPublisher publisher, IUnitOfWork unitOfWork) :
    IDomainEventHandler<OrderPlaced>,
    IDomainEventHandler<OrderCompleted>,
    IDomainEventHandler<OrderCancelled>
{
    public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(new OrderPlacedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            domainEvent.Order.TotalPrice,
            ToLines(domainEvent.Order)), cancellationToken);

    public Task HandleAsync(OrderCompleted domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(new OrderCompletedV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail), cancellationToken);

    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        PublishAsync(new OrderCancelledV1(
            Guid.NewGuid(),
            domainEvent.OccurredAtUtc,
            domainEvent.Order.Id,
            domainEvent.Order.CustomerEmail,
            ToLines(domainEvent.Order)), cancellationToken);

    private async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        publisher.Publish(integrationEvent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<OrderLineV1> ToLines(Order order) =>
        order.Items.Select(i => new OrderLineV1(i.ProductId, i.Quantity)).ToList();
}
