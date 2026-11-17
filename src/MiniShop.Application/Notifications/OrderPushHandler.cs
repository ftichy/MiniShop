using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Orders;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 23: oznámení push o objednávce – druhý kanál vedle e-mailu (<see cref="OrderNotificationsHandler"/>).
/// Samostatný handler = vlastní záznam v inboxu: když push služba selže a zpráva se zopakuje, e-mail neodejde podruhé.
/// Ordering se nezměnil – nový kanál je jen nový odběratel stejných událostí.
/// </summary>
/// <remarks>
/// Komu: ID účtu z uloženého příjemce (<c>OrderRecipient</c>, ukládá ho e-mailový handler z OrderPlacedV2). U potvrzení
/// může zpráva předběhnout vznik – pak výjimka a opakování (jako u e-mailu), nejvýš <see cref="OrderNotificationsHandler.MaxWaitForPlacedOrder"/>.
/// Objednávky bez příjemce (starší než Notifications, smazaný účet) oznámení nedostanou.
/// </remarks>
public sealed class OrderPushHandler(
    IOrderRecipientRepository recipients,
    PushNotifier notifier,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<OrderConfirmedV1>,
    IIntegrationMessageHandler<OrderRejectedV1>,
    IIntegrationMessageHandler<OrderShippedV1>,
    IIntegrationMessageHandler<OrderCompletedV1>,
    IIntegrationMessageHandler<OrderCancelledV1>,
    IIntegrationMessageHandler<ReturnRejectedV1>,
    IIntegrationMessageHandler<ReturnCompletedV1>,
    IIntegrationMessageHandler<ReturnRejectedV2>,
    IIntegrationMessageHandler<ReturnCompletedV2>
{
    public Task HandleAsync(OrderConfirmedV1 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, message.OccurredAtUtc, _ => PushTemplates.Confirmed(message.OrderId), cancellationToken);

    public Task HandleAsync(OrderRejectedV1 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, message.OccurredAtUtc, _ => PushTemplates.Rejected(message.OrderId), cancellationToken);

    public Task HandleAsync(OrderShippedV1 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, waitSinceUtc: null,
            pickup => PushTemplates.Shipped(message.OrderId, pickup, message.Carrier), cancellationToken);

    public Task HandleAsync(OrderCompletedV1 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, waitSinceUtc: null, pickup => PushTemplates.Delivered(message.OrderId, pickup), cancellationToken);

    public Task HandleAsync(OrderCancelledV1 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, waitSinceUtc: null, _ => PushTemplates.Cancelled(message.OrderId), cancellationToken);

    public Task HandleAsync(ReturnRejectedV1 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, waitSinceUtc: null, _ => PushTemplates.ReturnRejected(message.OrderId, 0), cancellationToken);

    public Task HandleAsync(ReturnCompletedV1 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, waitSinceUtc: null,
            _ => PushTemplates.ReturnCompleted(message.OrderId, 0, message.Amount, message.RefundedByCard), cancellationToken);

    public Task HandleAsync(ReturnRejectedV2 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, waitSinceUtc: null,
            _ => PushTemplates.ReturnRejected(message.OrderId, message.ReturnNumber), cancellationToken);

    public Task HandleAsync(ReturnCompletedV2 message, CancellationToken cancellationToken) =>
        SendAsync(message.OrderId, waitSinceUtc: null,
            _ => PushTemplates.ReturnCompleted(message.OrderId, message.ReturnNumber, message.Amount, message.RefundedByCard),
            cancellationToken);

    /// <param name="waitSinceUtc">Čas události, u které může předběhnout OrderPlacedV2 (potvrzení/zamítnutí) – pak čekat.</param>
    private async Task SendAsync(int orderId, DateTime? waitSinceUtc, Func<bool, PushMessage> message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        var recipient = await recipients.GetAsync(orderId, cancellationToken);
        if (recipient is null)
        {
            if (waitSinceUtc is { } occurredAtUtc
                && timeProvider.GetUtcNow().UtcDateTime - occurredAtUtc < OrderNotificationsHandler.MaxWaitForPlacedOrder)
            {
                throw new InvalidOperationException(
                    $"Pro objednávku {orderId} ještě nedorazilo OrderPlacedV2 (zpráva mimo pořadí) – zkusí se znovu.");
            }

            TraceTags.Tag("minishop.push.devices", 0);
            return;
        }

        await notifier.SendToCustomerAsync(recipient.CustomerId, message(recipient.IsPickup), cancellationToken);
    }
}
