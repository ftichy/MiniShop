using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 23: pošle oznámení na všechna zařízení zákazníka. Zařízení, které push služba už nezná (404/410),
/// rovnou smaže – jinak by se na něj zkoušelo posílat donekonečna.
/// </summary>
public sealed class PushNotifier(IPushSubscriptionRepository subscriptions, IPushSender sender, INotificationsUnitOfWork unitOfWork)
{
    /// <summary>
    /// Přechodná chyba (síť, 5xx) = výjimka → handler zprávu zopakuje. Zařízení, na která už došlo, dostanou oznámení
    /// znovu – stejný <see cref="PushMessage.Tag"/> ho na displeji nahradí, takže zákazník ho uvidí jednou.
    /// </summary>
    public async Task<int> SendToCustomerAsync(string customerId, PushMessage message, CancellationToken cancellationToken = default)
    {
        if (!sender.IsEnabled || string.IsNullOrEmpty(customerId))
        {
            return 0;
        }

        var devices = await subscriptions.ListForCustomerAsync(customerId, cancellationToken);
        var sent = 0;
        var gone = 0;
        foreach (var device in devices)
        {
            if (await sender.SendAsync(device, message, cancellationToken) == PushDeliveryResult.Sent)
            {
                sent++;
            }
            else
            {
                subscriptions.Remove(device);
                gone++;
            }
        }

        if (gone > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Na trasu jen počty – nikdy endpoint (identifikátor zařízení).
        TraceTags.Tag("minishop.push.devices", devices.Count);
        TraceTags.Tag("minishop.push.sent", sent);
        TraceTags.Tag("minishop.push.gone", gone);
        return sent;
    }

    /// <summary>
    /// Jako <see cref="SendToCustomerAsync"/>, ale chyba se jen poznamená na trasu – pro místa, kde je push doplněk
    /// k e-mailu ve stejném handleru (opakování by poslalo e-mail znovu). Podrobnosti chyby zaloguje odesílač.
    /// </summary>
    public async Task TrySendToCustomerAsync(string customerId, PushMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendToCustomerAsync(customerId, message, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            TraceTags.Tag("minishop.push.failed", true);
        }
    }
}
