using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Payments;
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
    IDomainEventHandler<OrderShipped>,
    IDomainEventHandler<OrderCompleted>,
    IDomainEventHandler<OrderCancelled>,
    IIntegrationMessageHandler<StockReservedV1>,
    IIntegrationMessageHandler<StockReservationFailedV1>,
    IIntegrationMessageHandler<StockReleasedV1>,
    IIntegrationMessageHandler<PaymentSucceededV1>,
    IIntegrationMessageHandler<PaymentFailedV1>
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

        // E-shop krok 8: platba kartou = po rezervaci ještě čekat na platbu (dobírka a API objednávky ne).
        sagas.Add(OrderSaga.Start(order.Id, UtcNow, settings.ReservationTimeout, order.RequiresOnlinePayment));
        publisher.Send(new ReserveStockV1(
            Guid.NewGuid(),
            UtcNow,
            order.Id,
            order.Items.Select(i => new StockLineV1(i.ProductId, i.Quantity)).ToList()));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task HandleAsync(StockReservedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.StockReserved(UtcNow, settings.PaymentTimeout), cancellationToken);

    public Task HandleAsync(StockReservationFailedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.StockReservationFailed(message.Reason, UtcNow), cancellationToken);

    public Task HandleAsync(StockReleasedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.StockReleased(UtcNow), cancellationToken);

    /// <summary>E-shop krok 8: brána potvrdila platbu → potvrdit objednávku (nebo vrátit peníze, když přišla pozdě).</summary>
    public Task HandleAsync(PaymentSucceededV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.PaymentSucceeded(UtcNow), cancellationToken);

    public Task HandleAsync(PaymentFailedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.PaymentFailed(message.Reason, UtcNow), cancellationToken);

    /// <summary>E-shop krok 6: zboží odešlo – sága už ho při zrušení nevrací (zrušit stejně nejde).</summary>
    public Task HandleAsync(OrderShipped domainEvent, CancellationToken cancellationToken) =>
        StepAsync(domainEvent.Order.Id, saga => saga.OrderShipped(UtcNow), cancellationToken);

    public Task HandleAsync(OrderCompleted domainEvent, CancellationToken cancellationToken) =>
        StepAsync(domainEvent.Order.Id, saga => saga.OrderCompleted(UtcNow), cancellationToken);

    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        StepAsync(domainEvent.Order.Id, saga => saga.OrderCancelled(UtcNow), cancellationToken);

    /// <summary>
    /// Volá OrderSagaTimeoutWorker pro ságy, které čekají na sklad (nebo od e-shop kroku 8 na platbu) příliš dlouho.
    /// Vrací true, když se objednávka opravdu zamítla (sklad nebo brána mezitím mohli odpovědět).
    /// </summary>
    public async Task<bool> TimeOutAsync(int orderId, CancellationToken cancellationToken) =>
        await StepAsync(orderId, saga => saga.TimedOut(UtcNow), cancellationToken)
            is OrderSagaAction.RejectOrder or OrderSagaAction.RejectOrderAndReleaseStock;

    private async Task<OrderSagaAction> StepAsync(int orderId, Func<OrderSaga, OrderSagaAction> transition,
        CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);   // span "process StockReservedV1", "dispatch OrderCancelled", "saga timeout"…
        var saga = await sagas.GetAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Sága objednávky {orderId} neexistuje.");
        var order = await GetOrderAsync(orderId, cancellationToken);

        // E-shop krok 9: zákazník mohl zrušit dřív, než sága dostala událost OrderCancelled (ta jde přes outbox) –
        // odpověď skladu/brány pak přijde první. Zrušení se nejdřív dožene, teprve pak se zpracuje odpověď,
        // takže sága nikdy nepotvrdí zrušenou objednávku. Pozdější OrderCancelled už nic neudělá (stav se změnil).
        if (order.Status == OrderStatus.Cancelled)
        {
            Execute(saga, order, saga.OrderCancelled(UtcNow));
        }

        var action = transition(saga);
        Execute(saga, order, action);

        // Souběh se zákazníkem (zrušil mezi načtením a uložením) = ConcurrencyConflictException na Order.Version →
        // zpráva se zopakuje a napodruhé už sága uvidí zrušenou objednávku.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return action;
    }

    private void Execute(OrderSaga saga, Order order, OrderSagaAction action)
    {
        switch (action)
        {
            case OrderSagaAction.ConfirmOrder:
                order.Confirm(UtcNow);                                                      // → OrderConfirmed → potvrzení zákazníkovi
                break;

            case OrderSagaAction.RejectOrder:
                order.Reject(saga.RejectionReason!, UtcNow);                                // → OrderRejected → omluva
                break;

            case OrderSagaAction.ReleaseStock:
                publisher.Send(new ReleaseStockV1(Guid.NewGuid(), UtcNow, order.Id));       // kompenzace
                break;

            case OrderSagaAction.RequestPayment:
                // E-shop krok 8: zboží drží rezervace, Payments připraví platbu (stránka brány) s termínem ságy.
                order.AwaitPayment(UtcNow);                                                 // → OrderAwaitingPayment
                publisher.Send(new RequestPaymentV1(
                    Guid.NewGuid(), UtcNow, order.Id, order.CustomerId, order.TotalPrice, saga.PaymentDeadlineUtc!.Value));
                break;

            case OrderSagaAction.RejectOrderAndReleaseStock:
                // E-shop krok 8: nezaplaceno – objednávku zamítnout a zboží vrátit, v jedné transakci (outbox).
                order.Reject(saga.RejectionReason!, UtcNow);
                publisher.Send(new ReleaseStockV1(Guid.NewGuid(), UtcNow, order.Id));
                break;

            case OrderSagaAction.RefundPayment:
                publisher.Send(new RefundPaymentV1(Guid.NewGuid(), UtcNow, order.Id));      // kompenzace pozdní platby
                break;

            case OrderSagaAction.ReleaseStockAndRefundPayment:
                // E-shop krok 9: zrušená zaplacená objednávka – zboží i peníze zpět, oba příkazy v jedné transakci.
                publisher.Send(new ReleaseStockV1(Guid.NewGuid(), UtcNow, order.Id));
                publisher.Send(new RefundPaymentV1(Guid.NewGuid(), UtcNow, order.Id));
                break;
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await orders.GetByIdAsync(orderId, cancellationToken)
        ?? throw new InvalidOperationException($"Objednávka {orderId} ze ságy neexistuje.");

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

/// <summary>Nastavení ságy (sekce Sagas v appsettings.json).</summary>
/// <param name="ReservationTimeout">Jak dlouho čekat na odpověď skladu, než se objednávka zamítne.</param>
/// <param name="PaymentTimeout">E-shop krok 8: jak dlouho čekat na platbu kartou (od rezervace), než se objednávka zamítne.</param>
public sealed record OrderSagaSettings(TimeSpan ReservationTimeout, TimeSpan PaymentTimeout);
