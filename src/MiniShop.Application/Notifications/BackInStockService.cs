using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>
/// „Dejte mi vědět“ – přihlášení a zrušení upozornění. Že je produkt vyprodaný a že je zákazník přihlášený s potvrzeným
/// e-mailem, ověřuje Web (Notifications o katalogu ani účtech neví); e-mail se při odeslání ověřuje znovu.
/// </summary>
public sealed class BackInStockService(IBackInStockRepository subscriptions, INotificationsUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<bool> IsSubscribedAsync(string customerId, int productId, CancellationToken cancellationToken = default) =>
        await subscriptions.FindAsync(customerId, productId, cancellationToken) is not null;

    /// <summary>Přihlásí k upozornění; už přihlášený zůstane (jeden lísteček na zákazníka a produkt).</summary>
    public async Task SubscribeAsync(string customerId, int productId, string productName, CancellationToken cancellationToken = default)
    {
        if (await subscriptions.FindAsync(customerId, productId, cancellationToken) is not null)
        {
            return;
        }

        subscriptions.Add(new BackInStockSubscription(customerId, productId, productName, timeProvider.GetUtcNow().UtcDateTime));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(string customerId, int productId, CancellationToken cancellationToken = default)
    {
        if (await subscriptions.FindAsync(customerId, productId, cancellationToken) is { } existing)
        {
            subscriptions.Remove(existing);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
