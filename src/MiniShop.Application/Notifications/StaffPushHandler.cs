using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 24: oznámení push k práci pro zaměstnance – nová objednávka k zabalení (sklad), žádost o vratku (podpora),
/// došlé zboží (sklad, sortiment). Další odběratel stejných událostí; vlastní handler = vlastní záznam v inboxu, výpadek
/// push služby nezopakuje e-maily ani oznámení zákazníkům.
/// </summary>
/// <remarks>
/// Komu: kdo si typ zaškrtl (<see cref="StaffAlertSubscription"/>) a podle <see cref="IStaffDirectory"/> k němu má TEĎ roli.
/// Přechodná chyba push služby = výjimka a opakování celé zprávy; zaměstnanci, kterým už oznámení došlo, ho dostanou znovu –
/// stejný tag ho na displeji nahradí.
/// </remarks>
public sealed class StaffPushHandler(
    IStaffAlertSubscriptionRepository subscriptions,
    IStaffDirectory staff,
    IProductNames productNames,
    PushNotifier notifier) :
    IIntegrationMessageHandler<OrderConfirmedV1>,
    IIntegrationMessageHandler<ReturnRequestedV1>,
    IIntegrationMessageHandler<ReturnRequestedV2>,
    IIntegrationMessageHandler<StockAvailabilityChangedV1>
{
    public Task HandleAsync(OrderConfirmedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        return SendAsync(StaffAlertKind.NewOrder, PushTemplates.StaffNewOrder(message.OrderId), cancellationToken);
    }

    public Task HandleAsync(ReturnRequestedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        return SendAsync(StaffAlertKind.ReturnRequested, PushTemplates.StaffReturnRequested(message.OrderId, 0), cancellationToken);
    }

    public Task HandleAsync(ReturnRequestedV2 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        return SendAsync(StaffAlertKind.ReturnRequested,
            PushTemplates.StaffReturnRequested(message.OrderId, message.ReturnNumber), cancellationToken);
    }

    public async Task HandleAsync(StockAvailabilityChangedV1 message, CancellationToken cancellationToken)
    {
        if (message.Availability != StockAvailabilityV1.OutOfStock)
        {
            return;
        }

        var name = await productNames.FindAsync(message.ProductId, cancellationToken);
        await SendAsync(StaffAlertKind.OutOfStock, PushTemplates.StaffOutOfStock(message.ProductId, name), cancellationToken);
    }

    private async Task SendAsync(StaffAlertKind kind, PushMessage message, CancellationToken cancellationToken)
    {
        var subscribed = await subscriptions.ListUserIdsAsync(kind, cancellationToken);
        var recipients = subscribed.Count == 0 ? [] : await staff.FilterAllowedAsync(kind, subscribed, cancellationToken);
        TraceTags.Tag("minishop.push.staff_recipients", recipients.Count);   // jen počet, žádná ID účtů

        foreach (var userId in recipients)
        {
            await notifier.SendToCustomerAsync(userId, message, cancellationToken);
        }
    }
}
