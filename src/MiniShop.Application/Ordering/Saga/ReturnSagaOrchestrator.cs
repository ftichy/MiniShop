using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Payments;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Application.Ordering.Saga;

/// <summary>
/// E-shop krok 12: orchestrátor vrácení zboží – druhý „dirigent“ vedle <see cref="OrderSagaOrchestrator"/>.
/// Řídí tři účastníky: admina (převzetí zásilky), sklad (naskladnění) a Payments (vrácení peněz).
/// E-shop krok 22: i vratky po částech – sága má klíč objednávka + číslo vratky a posílá V2 zprávy jen s vracenými kusy
/// a částkou (<see cref="RestockReturnedGoodsV2"/>, <see cref="RefundPaymentV2"/>). Vratka celé objednávky (číslo 0) beze změny.
/// </summary>
/// <remarks>
/// Stejný vzor jako objednávka: každý krok = jedna lokální transakce (sága + objednávka + odchozí příkaz v outboxu).
/// Kroky admina (<see cref="AcceptGoodsAsync"/>, <see cref="RejectAsync"/>) volá stránka Vratky přímo – jsou to
/// „zprávy od člověka“.
/// </remarks>
public sealed class ReturnSagaOrchestrator(
    IReturnSagaRepository sagas,
    IOrderRepository orders,
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    ReturnSettings settings,
    TimeProvider timeProvider) :
    IDomainEventHandler<OrderReturnRequested>,
    IDomainEventHandler<OrderPartialReturnRequested>,
    IIntegrationMessageHandler<ReturnedGoodsRestockedV1>,
    IIntegrationMessageHandler<ReturnedGoodsRestockedV2>,
    IIntegrationMessageHandler<PaymentRefundedV1>,
    IIntegrationMessageHandler<PaymentRefundedV2>
{
    /// <summary>Start ságy: zákazník požádal o vrácení celé objednávky → čekat na zboží (s termínem).</summary>
    public Task HandleAsync(OrderReturnRequested domainEvent, CancellationToken cancellationToken) =>
        StartAsync(domainEvent.Order, ReturnSaga.WholeOrder, domainEvent.OccurredAtUtc, cancellationToken);

    /// <summary>E-shop krok 22: start ságy vratky po částech.</summary>
    public Task HandleAsync(OrderPartialReturnRequested domainEvent, CancellationToken cancellationToken) =>
        StartAsync(domainEvent.Order, domainEvent.ReturnNumber, domainEvent.OccurredAtUtc, cancellationToken);

    /// <summary>Admin převzal vrácené zboží v pořádku. Vrací false, když vratka na zboží nečeká (už vyřízená).</summary>
    public async Task<bool> AcceptGoodsAsync(int orderId, int returnNumber = ReturnSaga.WholeOrder,
        CancellationToken cancellationToken = default) =>
        await sagas.GetAsync(orderId, returnNumber, cancellationToken) is not null
        && await StepAsync(orderId, returnNumber, saga => saga.GoodsReceived(UtcNow), cancellationToken) != ReturnSagaAction.None;

    /// <summary>Admin vrácení zamítl (s důvodem pro zákazníka). Neexistující vratka = false (formulář ze staré stránky).</summary>
    public async Task<bool> RejectAsync(int orderId, string reason, int returnNumber = ReturnSaga.WholeOrder,
        CancellationToken cancellationToken = default) =>
        await sagas.GetAsync(orderId, returnNumber, cancellationToken) is not null
        && await StepAsync(orderId, returnNumber, saga => saga.Reject(reason, UtcNow), cancellationToken) != ReturnSagaAction.None;

    /// <summary>Volá OrderSagaTimeoutWorker pro vratky, kde zboží nedorazilo včas. True = vrácení vypršelo.</summary>
    public async Task<bool> TimeOutAsync(int orderId, int returnNumber = ReturnSaga.WholeOrder,
        CancellationToken cancellationToken = default) =>
        await StepAsync(orderId, returnNumber, saga => saga.TimedOut(UtcNow), cancellationToken) == ReturnSagaAction.RejectReturn;

    public Task HandleAsync(ReturnedGoodsRestockedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, ReturnSaga.WholeOrder, saga => saga.GoodsRestocked(UtcNow), cancellationToken);

    /// <summary>E-shop krok 22: sklad naskladnil kusy vratky po částech.</summary>
    public Task HandleAsync(ReturnedGoodsRestockedV2 message, CancellationToken cancellationToken) =>
        StepIfExistsAsync(message.OrderId, message.ReturnNumber, saga => saga.GoodsRestocked(UtcNow), cancellationToken);

    /// <summary>
    /// Peníze vráceny. Pozor: PaymentRefundedV1 chodí i za pozdní platby a zrušení (sága objednávky) – bez vratky se nic neděje.
    /// </summary>
    public async Task HandleAsync(PaymentRefundedV1 message, CancellationToken cancellationToken)
    {
        if (await sagas.GetAsync(message.OrderId, ReturnSaga.WholeOrder, cancellationToken) is null)
        {
            return;
        }

        await StepAsync(message.OrderId, ReturnSaga.WholeOrder, saga => saga.PaymentRefunded(UtcNow), cancellationToken);
    }

    /// <summary>E-shop krok 22: Payments vrátil částku vratky po částech.</summary>
    public Task HandleAsync(PaymentRefundedV2 message, CancellationToken cancellationToken) =>
        StepIfExistsAsync(message.OrderId, message.ReturnNumber, saga => saga.PaymentRefunded(UtcNow), cancellationToken);

    /// <summary>Odpověď bez ságy (ruční zásah, zpráva z jiného zdroje) se ignoruje – stejně jako PaymentRefundedV1.</summary>
    private async Task StepIfExistsAsync(int orderId, int returnNumber, Func<ReturnSaga, ReturnSagaAction> transition,
        CancellationToken cancellationToken)
    {
        if (await sagas.GetAsync(orderId, returnNumber, cancellationToken) is not null)
        {
            await StepAsync(orderId, returnNumber, transition, cancellationToken);
        }
    }

    /// <summary>Rozpracované vratky pro stránku Vratky.</summary>
    public Task<IReadOnlyList<ReturnSaga>> GetOpenAsync(CancellationToken cancellationToken = default) =>
        sagas.GetOpenAsync(cancellationToken);

    private async Task StartAsync(Order order, int returnNumber, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(order.Id);
        TraceTags.Tag("minishop.return.number", returnNumber);
        if (await sagas.GetAsync(order.Id, returnNumber, cancellationToken) is not null)
        {
            return;   // sága už běží
        }

        sagas.Add(ReturnSaga.Start(order.Id, occurredAtUtc, settings.SendWindow,
            requiresRefund: order.PaymentMethod == PaymentMethod.Card, returnNumber));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<ReturnSagaAction> StepAsync(int orderId, int returnNumber, Func<ReturnSaga, ReturnSagaAction> transition,
        CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        TraceTags.Tag("minishop.return.number", returnNumber);
        var saga = await sagas.GetAsync(orderId, returnNumber, cancellationToken)
            ?? throw new InvalidOperationException($"Sága vratky {returnNumber} objednávky {orderId} neexistuje.");
        var stateBefore = saga.State;

        var action = transition(saga);
        switch (action)
        {
            case ReturnSagaAction.RestockGoods when saga.IsPartial:
                var restocked = (await GetOrderAsync(orderId, cancellationToken)).GetReturn(returnNumber);
                publisher.Send(new RestockReturnedGoodsV2(Guid.NewGuid(), UtcNow, orderId, returnNumber,
                    restocked.Lines.Select(l => new StockLineV1(l.ProductId, l.Quantity)).ToList()));
                break;

            case ReturnSagaAction.RestockGoods:
                publisher.Send(new RestockReturnedGoodsV1(Guid.NewGuid(), UtcNow, orderId));
                break;

            case ReturnSagaAction.RejectReturn when saga.IsPartial:
                (await GetOrderAsync(orderId, cancellationToken))
                    .RejectPartialReturn(returnNumber, saga.RejectionReason!, UtcNow);   // → ReturnRejectedV2
                break;

            case ReturnSagaAction.RejectReturn:
                (await GetOrderAsync(orderId, cancellationToken)).RejectReturn(saga.RejectionReason!, UtcNow);   // → ReturnRejectedV1
                break;

            case ReturnSagaAction.RefundPayment when saga.IsPartial:
                var refunded = (await GetOrderAsync(orderId, cancellationToken)).GetReturn(returnNumber);
                publisher.Send(new RefundPaymentV2(Guid.NewGuid(), UtcNow, orderId, returnNumber, refunded.Amount));   // jen částka vratky
                break;

            case ReturnSagaAction.RefundPayment:
                publisher.Send(new RefundPaymentV1(Guid.NewGuid(), UtcNow, orderId));     // stejný příkaz jako u pozdní platby
                break;

            case ReturnSagaAction.CompleteReturn when saga.IsPartial:
                (await GetOrderAsync(orderId, cancellationToken)).CompletePartialReturn(returnNumber, UtcNow);   // → ReturnCompletedV2
                break;

            case ReturnSagaAction.CompleteReturn:
                (await GetOrderAsync(orderId, cancellationToken)).CompleteReturn(UtcNow);   // → ReturnCompletedV1
                break;
        }

        // Souběh (admin × timeout) = concurrency token na ReturnSaga.Version → ConcurrencyConflictException.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TraceTags.Tag("minishop.return_saga.state_before", stateBefore.ToString());   // na trasu: stav vratky před → po, akce
        TraceTags.Tag("minishop.return_saga.state", saga.State.ToString());
        TraceTags.Tag("minishop.return_saga.action", action.ToString());
        return action;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await orders.GetByIdAsync(orderId, cancellationToken)
        ?? throw new InvalidOperationException($"Objednávka {orderId} z vratky neexistuje.");

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

/// <summary>E-shop krok 12: nastavení vratek (sekce Returns v appsettings.json).</summary>
/// <param name="Window">Do kdy od doručení smí zákazník požádat o vrácení (14 dní).</param>
/// <param name="SendWindow">Kolik času má zákazník na odeslání zboží od žádosti (14 dní), pak vrácení vyprší.</param>
public sealed record ReturnSettings(TimeSpan Window, TimeSpan SendWindow);
