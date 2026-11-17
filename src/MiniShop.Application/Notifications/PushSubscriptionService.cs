using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>Zařízení zákazníka v nastavení oznámení (bez endpointu – ten se na stránku neposílá).</summary>
public sealed record PushDeviceDto(int Id, string DeviceLabel, DateTime CreatedAtUtc);

/// <summary>Co prohlížeč pošle po souhlasu s oznámeními (<c>PushSubscription.toJSON()</c> + popisek zařízení).</summary>
public sealed record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);

/// <summary>
/// E-shop krok 23: zapnutí a vypnutí oznámení push na zařízení. Že jde o přihlášeného zákazníka, ověřuje Web
/// (Notifications o účtech neví).
/// </summary>
public sealed class PushSubscriptionService(
    IPushSubscriptionRepository subscriptions,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Zákazník může mít víc zařízení, ale ne nekonečně (každé = jedno odeslání u každé události).</summary>
    public const int MaxDevicesPerCustomer = 10;

    public async Task<IReadOnlyList<PushDeviceDto>> ListAsync(string customerId, CancellationToken cancellationToken = default) =>
        (await subscriptions.ListForCustomerAsync(customerId, cancellationToken))
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new PushDeviceDto(s.Id, s.DeviceLabel, s.CreatedAtUtc))
            .ToList();

    /// <summary>
    /// Zapne oznámení na zařízení. Stejné zařízení podruhé = obnoví klíče (a převezme ho aktuální účet). Nad limit zařízení
    /// se nejstarší smaže.
    /// </summary>
    public async Task SubscribeAsync(string customerId, PushSubscribeRequest request, string deviceLabel,
        CancellationToken cancellationToken = default)
    {
        if (await subscriptions.FindByEndpointAsync(request.Endpoint, cancellationToken) is { } existing)
        {
            existing.Renew(customerId, request.P256dh, request.Auth, deviceLabel);
        }
        else
        {
            subscriptions.Add(new PushSubscription(customerId, request.Endpoint, request.P256dh, request.Auth, deviceLabel,
                timeProvider.GetUtcNow().UtcDateTime));

            var devices = await subscriptions.ListForCustomerAsync(customerId, cancellationToken);
            foreach (var oldest in devices.OrderByDescending(s => s.CreatedAtUtc).Skip(MaxDevicesPerCustomer - 1))
            {
                subscriptions.Remove(oldest);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Vypnout na tomto zařízení (prohlížeč pošle svůj endpoint). Cizí zařízení se nesmaže.</summary>
    public async Task UnsubscribeAsync(string customerId, string endpoint, CancellationToken cancellationToken = default)
    {
        if (await subscriptions.FindByEndpointAsync(endpoint, cancellationToken) is { } existing && existing.CustomerId == customerId)
        {
            subscriptions.Remove(existing);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Odebrat zařízení ze seznamu (např. ztracený telefon). Cizí zařízení = nic.</summary>
    public async Task RemoveAsync(string customerId, int id, CancellationToken cancellationToken = default)
    {
        if (await subscriptions.FindAsync(id, cancellationToken) is { } existing && existing.CustomerId == customerId)
        {
            subscriptions.Remove(existing);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
