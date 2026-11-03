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
/// </summary>
/// <remarks>
/// Stejný vzor jako objednávka: každý krok = jedna lokální transakce (sága + objednávka + odchozí příkaz v outboxu).
/// Kroky admina (<see cref="AcceptGoodsAsync"/>, <see cref="RejectAsync"/>) volá stránka Vratky přímo – jsou to
/// „zprávy od člověka“. Platby se vrací stávajícím příkazem RefundPaymentV1 – Payments o vratkách nic neví.
/// </remarks>
public sealed class ReturnSagaOrchestrator(
    IReturnSagaRepository sagas,
    IOrderRepository orders,
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    ReturnSettings settings,
    TimeProvider timeProvider) :
    IDomainEventHandler<OrderReturnRequested>,
    IIntegrationMessageHandler<ReturnedGoodsRestockedV1>,
    IIntegrationMessageHandler<PaymentRefundedV1>
{
    /// <summary>Start ságy: zákazník požádal o vrácení → čekat na zboží (s termínem).</summary>
    public async Task HandleAsync(OrderReturnRequested domainEvent, CancellationToken cancellationToken)
    {
        var order = domainEvent.Order;
        TraceTags.TagOrder(order.Id);
        if (await sagas.GetAsync(order.Id, cancellationToken) is not null)
        {
            return;   // sága už běží
        }

        sagas.Add(ReturnSaga.Start(order.Id, domainEvent.OccurredAtUtc, settings.SendWindow,
            requiresRefund: order.PaymentMethod == PaymentMethod.Card));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Admin převzal vrácené zboží v pořádku. Vrací false, když vratka na zboží nečeká (už vyřízená).</summary>
    public async Task<bool> AcceptGoodsAsync(int orderId, CancellationToken cancellationToken = default) =>
        await sagas.GetAsync(orderId, cancellationToken) is not null
        && await StepAsync(orderId, saga => saga.GoodsReceived(UtcNow), cancellationToken) != ReturnSagaAction.None;

    /// <summary>Admin vrácení zamítl (s důvodem pro zákazníka). Neexistující vratka = false (formulář ze staré stránky).</summary>
    public async Task<bool> RejectAsync(int orderId, string reason, CancellationToken cancellationToken = default) =>
        await sagas.GetAsync(orderId, cancellationToken) is not null
        && await StepAsync(orderId, saga => saga.Reject(reason, UtcNow), cancellationToken) != ReturnSagaAction.None;

    /// <summary>Volá OrderSagaTimeoutWorker pro vratky, kde zboží nedorazilo včas. True = vrácení vypršelo.</summary>
    public async Task<bool> TimeOutAsync(int orderId, CancellationToken cancellationToken = default) =>
        await StepAsync(orderId, saga => saga.TimedOut(UtcNow), cancellationToken) == ReturnSagaAction.RejectReturn;

    public Task HandleAsync(ReturnedGoodsRestockedV1 message, CancellationToken cancellationToken) =>
        StepAsync(message.OrderId, saga => saga.GoodsRestocked(UtcNow), cancellationToken);

    /// <summary>
    /// Peníze vráceny. Pozor: PaymentRefundedV1 chodí i za pozdní platby a zrušení (sága objednávky) – bez vratky se nic neděje.
    /// </summary>
    public async Task HandleAsync(PaymentRefundedV1 message, CancellationToken cancellationToken)
    {
        if (await sagas.GetAsync(message.OrderId, cancellationToken) is null)
        {
            return;
        }

        await StepAsync(message.OrderId, saga => saga.PaymentRefunded(UtcNow), cancellationToken);
    }

    /// <summary>Rozpracované vratky pro stránku Vratky.</summary>
    public Task<IReadOnlyList<ReturnSaga>> GetOpenAsync(CancellationToken cancellationToken = default) =>
        sagas.GetOpenAsync(cancellationToken);

    private async Task<ReturnSagaAction> StepAsync(int orderId, Func<ReturnSaga, ReturnSagaAction> transition,
        CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        var saga = await sagas.GetAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Sága vratky objednávky {orderId} neexistuje.");

        var action = transition(saga);
        switch (action)
        {
            case ReturnSagaAction.RestockGoods:
                publisher.Send(new RestockReturnedGoodsV1(Guid.NewGuid(), UtcNow, orderId));
                break;

            case ReturnSagaAction.RejectReturn:
                (await GetOrderAsync(orderId, cancellationToken)).RejectReturn(saga.RejectionReason!, UtcNow);   // → ReturnRejectedV1
                break;

            case ReturnSagaAction.RefundPayment:
                publisher.Send(new RefundPaymentV1(Guid.NewGuid(), UtcNow, orderId));     // stejný příkaz jako u pozdní platby
                break;

            case ReturnSagaAction.CompleteReturn:
                (await GetOrderAsync(orderId, cancellationToken)).CompleteReturn(UtcNow);   // → ReturnCompletedV1
                break;
        }

        // Souběh (admin × timeout) = concurrency token na ReturnSaga.Version → ConcurrencyConflictException.
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
