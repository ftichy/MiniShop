using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;

namespace MiniShop.Application.Notifications;

/// <summary>
/// Produkt je zase skladem (<see cref="StockAvailabilityChangedV1"/> → Skladem / Poslední kusy): každému přihlášenému
/// pošle jeden e-mail a jeho lísteček smaže. E-mail dostane jen zákazník, který existuje a má potvrzený e-mail.
/// </summary>
/// <remarks>
/// Po každém zákazníkovi se ukládá zvlášť (odeslat → smazat → uložit): když odeslání jednoho selže, zpráva se zopakuje
/// a už obsloužení zákazníci nedostanou e-mail znovu. Známé omezení: pád přesně mezi odesláním a uložením = e-mail znovu.
/// </remarks>
public sealed class BackInStockHandler(
    IBackInStockRepository subscriptions,
    ICustomerContacts contacts,
    IEmailSender emailSender,
    BackInStockEmailTemplates templates,
    INotificationsUnitOfWork unitOfWork) : IIntegrationMessageHandler<StockAvailabilityChangedV1>
{
    public async Task HandleAsync(StockAvailabilityChangedV1 message, CancellationToken cancellationToken)
    {
        if (message.Availability is not (StockAvailabilityV1.InStock or StockAvailabilityV1.LowStock))
        {
            return;
        }

        foreach (var subscription in await subscriptions.ListForProductAsync(message.ProductId, cancellationToken))
        {
            var contact = await contacts.FindAsync(subscription.CustomerId, cancellationToken);
            if (contact is { EmailConfirmed: true, Email.Length: > 0 })
            {
                await emailSender.SendAsync(templates.BackInStock(contact, subscription.ProductId, subscription.ProductName), cancellationToken);
            }

            subscriptions.Remove(subscription);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
