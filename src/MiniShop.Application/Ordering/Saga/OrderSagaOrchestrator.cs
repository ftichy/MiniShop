using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Application.Ordering.Saga;

/// <summary>
/// Orchestrátor ságy objednávky (krok 9): "dirigent", který řídí rezervaci skladu.
/// Poslouchá události objednávky (doménové) i odpovědi skladu (integrační), posouvá stavový automat <see cref="OrderSaga"/>
/// a podle výsledku pošle skladu příkaz nebo potvrdí / zamítne objednávku.
/// </summary>
/// <remarks>
/// Celý průběh je na jednom místě – stačí se podívat do tabulky OrderSagas a je jasné, kde objednávka "visí".
/// Každý krok je jedna lokální transakce: změna ságy + objednávky + odchozí příkaz v outboxu (+ odškrtnutí v inboxu).
/// Sklad nikdy nevolá přímo – jen mu posílá příkazy přes outbox.
/// </remarks>
public sealed class OrderSagaOrchestrator(
    IOrderSagaRepository sagas,
    IOrderRepository orders,
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    OrderSagaSettings settings,
    TimeProvider timeProvider) :
    IDomainEventHandler<OrderPlaced>,
    IDomainEventHandler<OrderCompleted>,
    IDomainEventHandler<OrderCancelled>,
    IIntegrationMessageHandler<StockReservedV1>,
    IIntegrationMessageHandler<StockReservationFailedV1>,
    IIntegrationMessageHandler<StockReleasedV1>
{
    /// <summary>Start ságy: objednávka vznikla → zapsat si to a požádat sklad o rezervaci.</summary>
    public async Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        var order = domainEvent.Order;
        TraceTags.TagOrder(order.Id);
        if (await sagas.GetAsync(order.Id, cancellationToken) is not null)
        {
            return;   // sága už běží
        }

        sagas.Add(OrderSaga.Start(order.Id, UtcNow, settings.ReservationTimeout));
        publisher.Send(new ReserveStockV1(
            Guid.NewGuid(),
            UtcNow,
            order.Id,
            order.Items.Select(i => new StockLineV1(i.ProductId, i.Quantity)).ToList()));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task HandleAsync(StockReservedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.StockReserved(UtcNow), cancellationToken);

    public Task HandleAsync(StockReservationFailedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.StockReservationFailed(message.Reason, UtcNow), cancellationToken);

    public Task HandleAsync(StockReleasedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.StockReleased(UtcNow), cancellationToken);

    public Task HandleAsync(OrderCompleted domainEvent, CancellationToken cancellationToken) =>
        StepAsync(domainEvent.Order.Id, saga => saga.OrderCompleted(UtcNow), cancellationToken);

    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        StepAsync(domainEvent.Order.Id, saga => saga.OrderCancelled(UtcNow), cancellationToken);

    /// <summary>
    /// Volá OrderSagaTimeoutWorker pro ságy, které čekají na sklad příliš dlouho.
    /// Vrací true, když se objednávka opravdu zamítla (sklad mezitím mohl odpovědět).
    /// </summary>
    public async Task<bool> TimeOutAsync(int orderId, CancellationToken cancellationToken) =>
        await StepAsync(orderId, saga => saga.ReservationTimedOut(UtcNow), cancellationToken) == OrderSagaAction.RejectOrder;

    private async Task<OrderSagaAction> StepAsync(int orderId, Func<OrderSaga, OrderSagaAction> transition,
        CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);   // span "process StockReservedV1", "dispatch OrderCancelled", "saga timeout"…
        var saga = await sagas.GetAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Sága objednávky {orderId} neexistuje.");

        var action = transition(saga);
        switch (action)
        {
            case OrderSagaAction.ConfirmOrder:
                (await GetOrderAsync(orderId, cancellationToken)).Confirm(UtcNow);          // → OrderConfirmed → potvrzení zákazníkovi
                break;

            case OrderSagaAction.RejectOrder:
                (await GetOrderAsync(orderId, cancellationToken)).Reject(saga.RejectionReason!, UtcNow);   // → OrderRejected → omluva
                break;

            case OrderSagaAction.ReleaseStock:
                publisher.Send(new ReleaseStockV1(Guid.NewGuid(), UtcNow, orderId));       // kompenzace
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return action;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await orders.GetByIdAsync(orderId, cancellationToken)
        ?? throw new InvalidOperationException($"Objednávka {orderId} ze ságy neexistuje.");

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

/// <summary>Nastavení ságy (sekce Sagas v appsettings.json).</summary>
/// <param name="ReservationTimeout">Jak dlouho čekat na odpověď skladu, než se objednávka zamítne.</param>
public sealed record OrderSagaSettings(TimeSpan ReservationTimeout);
