using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Notifications;
using AppPushMessage = MiniShop.Application.Notifications.PushMessage;
using DomainPushSubscription = MiniShop.Domain.Notifications.PushSubscription;

namespace MiniShop.Infrastructure.Notifications;

/// <summary>
/// E-shop krok 23: klíče VAPID (konfigurace <c>PushNotifications:*</c>). Pár klíčů P-256 v base64url – veřejný (65 bajtů)
/// zná prohlížeč, soukromým (32 bajtů) MiniShop podepisuje každou zprávu, aby push služba věděla, že je od nás.
/// Vytvoří je příkaz <c>dotnet MiniShop.Web.dll generate-vapid-keys</c>. Bez klíčů jsou oznámení push vypnutá.
/// </summary>
public sealed record PushSettings(string? PublicKey, string? PrivateKey, string Subject)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);

    /// <summary>
    /// Načte a ověří nastavení. Vyplněný jen jeden klíč nebo klíč špatného tvaru = výjimka (Web se nespustí) – raději hned
    /// než tichá oznámení, která nikdy nedorazí.
    /// </summary>
    public static PushSettings FromValues(string? publicKey, string? privateKey, string? publicBaseUrl)
    {
        var settings = new PushSettings(Blank(publicKey), Blank(privateKey),
            string.IsNullOrWhiteSpace(publicBaseUrl) ? "mailto:minishop@localhost" : publicBaseUrl.TrimEnd('/'));
        if (settings.PublicKey is null && settings.PrivateKey is null)
        {
            return settings;
        }

        if (settings.PublicKey is null || settings.PrivateKey is null)
        {
            throw new InvalidOperationException("PushNotifications: vyplňte oba klíče VAPID (VapidPublicKey i VapidPrivateKey), nebo žádný.");
        }

        if (DecodedLength(settings.PublicKey) != 65 || DecodedLength(settings.PrivateKey) != 32)
        {
            throw new InvalidOperationException(
                "PushNotifications: klíče VAPID nemají správný tvar (base64url, veřejný 65 bajtů, soukromý 32) – vytvořte je " +
                "příkazem generate-vapid-keys.");
        }

        return settings;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int DecodedLength(string base64Url)
    {
        try
        {
            var base64 = base64Url.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=')).Length;
        }
        catch (FormatException)
        {
            return -1;
        }
    }
}

/// <summary>
/// E-shop krok 23: odeslání oznámení přes Web Push (RFC 8030/8291/8292) knihovnou Lib.Net.Http.WebPush – obsah zašifruje
/// klíči prohlížeče (push služba ho nepřečte) a připojí podpis VAPID.
/// </summary>
/// <remarks>
/// Endpoint (adresa zařízení u push služby) je identifikátor zařízení – nikdy do logu. Trasy: adresu požadavku na push
/// službu zkracuje ServiceDefaults (<c>PushServiceHosts</c>) na jméno serveru.
/// </remarks>
public sealed class WebPushSender(HttpClient httpClient, PushSettings settings, ILogger<WebPushSender> logger)
    : IPushSender
{

    /// <summary>Jak dlouho push služba zprávu drží, když je telefon offline (pak ji zahodí – oznámení je jen doplněk e-mailu).</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => true;

    public string? PublicKey => settings.PublicKey;

    public async Task<PushDeliveryResult> SendAsync(DomainPushSubscription subscription, AppPushMessage message,
        CancellationToken cancellationToken = default)
    {
        var target = new PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        // Obsah čte jen náš service worker (sw.js, událost push).
        var content = JsonSerializer.Serialize(new { message.Title, message.Body, message.Url, message.Tag }, Json);
        var client = new PushServiceClient(httpClient)
        {
            DefaultAuthentication = new VapidAuthentication(settings.PublicKey, settings.PrivateKey) { Subject = settings.Subject },
            AutoRetryAfter = false   // opakování řeší odolnost HttpClientu a fronta zpráv, ne čekání tady
        };

        try
        {
            await client.RequestPushMessageDeliveryAsync(target,
                new Lib.Net.Http.WebPush.PushMessage(content) { TimeToLive = (int)TimeToLive.TotalSeconds, Urgency = PushMessageUrgency.Normal },
                cancellationToken);
            return PushDeliveryResult.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Zařízení už neexistuje (oznámení vypnutá, aplikace odinstalovaná) – volající záznam smaže.
            logger.LogInformation("Push: zařízení {SubscriptionId} už neexistuje ({StatusCode}) – smažu ho", subscription.Id, (int)ex.StatusCode);
            return PushDeliveryResult.Gone;
        }
        catch (PushServiceClientException ex)
        {
            logger.LogWarning("Push: push služba odmítla zprávu pro zařízení {SubscriptionId} ({StatusCode})", subscription.Id, (int)ex.StatusCode);
            throw;
        }
    }
}

/// <summary>E-shop krok 23: bez klíčů VAPID se oznámení push nenabízejí ani neposílají.</summary>
public sealed class DisabledPushSender : IPushSender
{
    public bool IsEnabled => false;

    public string? PublicKey => null;

    public Task<PushDeliveryResult> SendAsync(DomainPushSubscription subscription, AppPushMessage message,
        CancellationToken cancellationToken = default) => Task.FromResult(PushDeliveryResult.Sent);
}
