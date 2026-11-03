using Microsoft.Extensions.Logging;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// Pokusný handler z kroku 2: jen zapíše do logu, že událost proběhla.
/// Ukazuje, že jedna třída může obsluhovat víc událostí.
/// </summary>
public sealed class OrderEventsLogHandler(ILogger<OrderEventsLogHandler> logger) :
    IDomainEventHandler<OrderPlaced>,
    IDomainEventHandler<OrderConfirmed>,
    IDomainEventHandler<OrderRejected>,
    IDomainEventHandler<OrderCompleted>,
    IDomainEventHandler<OrderCancelled>,
    IDomainEventHandler<OrderPackingStarted>,
    IDomainEventHandler<OrderShipped>,
    IDomainEventHandler<OrderAwaitingPayment>
{
    public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        // Dispatch běží až po uložení, takže objednávka už má přidělené ID.
        logger.LogInformation("[Událost] OrderPlaced: objednávka {OrderId}, {ItemCount} položek, celkem {Total} Kč",
            domainEvent.Order.Id, domainEvent.Order.Items.Count, domainEvent.Order.TotalPrice);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderConfirmed domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Událost] OrderConfirmed: objednávka {OrderId}", domainEvent.Order.Id);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderRejected domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Událost] OrderRejected: objednávka {OrderId} – {Reason}",
            domainEvent.Order.Id, domainEvent.Order.RejectionReason);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderCompleted domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Událost] OrderCompleted: objednávka {OrderId}", domainEvent.Order.Id);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Událost] OrderCancelled: objednávka {OrderId}", domainEvent.Order.Id);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderPackingStarted domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Událost] OrderPackingStarted: objednávka {OrderId}", domainEvent.Order.Id);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderShipped domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Událost] OrderShipped: objednávka {OrderId}, dopravce {Carrier}",
            domainEvent.Order.Id, domainEvent.Order.Carrier);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderAwaitingPayment domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Událost] OrderAwaitingPayment: objednávka {OrderId} čeká na platbu", domainEvent.Order.Id);
        return Task.CompletedTask;
    }
}
