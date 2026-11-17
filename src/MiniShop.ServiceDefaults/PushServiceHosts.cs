using System.Diagnostics;

namespace MiniShop.ServiceDefaults;

/// <summary>
/// E-shop krok 23: oznámení push jdou na push služby prohlížečů. Adresa požadavku (endpoint) obsahuje jedinečný
/// identifikátor zařízení zákazníka = osobní údaj – do trasy (Grafana Cloud) nesmí. Span HTTP klienta proto dostane
/// jen schéma a jméno serveru.
/// </summary>
public static class PushServiceHosts
{
    /// <summary>Push služby Chrome/Edge/Android (FCM), Firefoxu, Safari/iOS a Windows.</summary>
    private static readonly string[] HostSuffixes =
    [
        "fcm.googleapis.com",
        "push.services.mozilla.com",
        "push.apple.com",
        "notify.windows.com"
    ];

    public static bool IsPushService(Uri? uri) =>
        uri is not null && HostSuffixes.Any(suffix =>
            uri.Host.Equals(suffix, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Přepíše <c>url.full</c> na <c>https://server/…</c> (volá instrumentace HttpClientu).</summary>
    public static void Redact(Activity activity, HttpRequestMessage request)
    {
        if (IsPushService(request.RequestUri))
        {
            activity.SetTag("url.full", $"{request.RequestUri!.Scheme}://{request.RequestUri.Host}/…");
        }
    }
}
