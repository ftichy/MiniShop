using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>E-shop krok 23: oznámení push – co se ukáže na displeji.</summary>
/// <param name="Title">Nadpis (krátký – displej ho zkrátí).</param>
/// <param name="Body">Text – bez osobních údajů (jméno, adresa); stačí číslo objednávky a stav.</param>
/// <param name="Url">Kam vede klepnutí (cesta na webu, např. /Orders/12).</param>
/// <param name="Tag">Stejný tag nahradí starší oznámení – opakované doručení se na displeji neukáže dvakrát.</param>
public sealed record PushMessage(string Title, string Body, string Url, string Tag);

/// <summary>Výsledek doručení na jedno zařízení.</summary>
public enum PushDeliveryResult
{
    /// <summary>Push služba zprávu převzala.</summary>
    Sent,

    /// <summary>Zařízení už neexistuje (404/410 – oznámení vypnutá, aplikace odinstalovaná) → záznam smazat.</summary>
    Gone
}

/// <summary>
/// E-shop krok 23: odeslání zprávy push službě prohlížeče (Web Push – šifrování aes128gcm, podpis VAPID).
/// Implementace v Infrastructure (<c>WebPushSender</c>); bez klíčů VAPID <c>DisabledPushSender</c>.
/// Přechodná chyba (síť, 5xx) = výjimka – zpráva se zopakuje.
/// </summary>
public interface IPushSender
{
    /// <summary>Jsou nastavené klíče VAPID (jinak se oznámení nenabízí).</summary>
    bool IsEnabled { get; }

    /// <summary>Veřejný klíč VAPID pro prohlížeč (<c>applicationServerKey</c>), nebo null.</summary>
    string? PublicKey { get; }

    Task<PushDeliveryResult> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken = default);
}

/// <summary>E-shop krok 23: zařízení pro oznámení push (modul Notifications).</summary>
public interface IPushSubscriptionRepository
{
    Task<PushSubscription?> FindByEndpointAsync(string endpoint, CancellationToken cancellationToken = default);

    Task<PushSubscription?> FindAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PushSubscription>> ListForCustomerAsync(string customerId, CancellationToken cancellationToken = default);

    void Add(PushSubscription subscription);

    void Remove(PushSubscription subscription);

    /// <summary>Zapomenout zákazníka (smazal účet).</summary>
    Task<int> RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}
