using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Notifications;

/// <summary>
/// E-shop krok 23: zařízení, na které zákazník chce oznámení push („schránka“ prohlížeče u jeho push služby – Google FCM,
/// Apple, Mozilla). Prohlížeč ji vytvoří po souhlasu zákazníka; MiniShop si pamatuje adresu (<see cref="Endpoint"/>)
/// a dva klíče, kterými zprávu zašifruje – push služba obsah nevidí.
/// </summary>
/// <remarks>
/// Endpoint je jedinečný identifikátor zařízení = osobní údaj: nikdy do logu ani trasy; po smazání účtu se smaže
/// (CustomerAnonymizedV1). Když push služba odpoví „už neexistuje“ (404/410 – oznámení vypnutá, aplikace odinstalovaná),
/// záznam se smaže taky.
/// </remarks>
public class PushSubscription
{
    public const int MaxEndpointLength = 2048;
    public const int MaxKeyLength = 256;
    public const int MaxDeviceLabelLength = 100;

    // Konstruktor pro EF Core.
    private PushSubscription()
    {
    }

    public PushSubscription(string customerId, string endpoint, string p256dh, string auth, string deviceLabel, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Oznámení push potřebuje zákazníka.");
        }

        CustomerId = customerId;
        Endpoint = ValidEndpoint(endpoint);
        SetKeys(p256dh, auth);
        DeviceLabel = Truncate(deviceLabel);
        CreatedAtUtc = createdAtUtc;
    }

    public int Id { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>Adresa push služby pro toto zařízení (https).</summary>
    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>Veřejný klíč prohlížeče (ECDH P-256, base64url) – šifrování obsahu.</summary>
    public string P256dh { get; private set; } = string.Empty;

    /// <summary>Sdílené tajemství prohlížeče (base64url) – šifrování obsahu.</summary>
    public string Auth { get; private set; } = string.Empty;

    /// <summary>Popisek pro zákazníka, např. „Android · Chrome“ (z User-Agent, celý se neukládá).</summary>
    public string DeviceLabel { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Stejné zařízení se přihlásí znovu (nové klíče), nebo se na něm přihlásil jiný účet – zařízení patří tomu, kdo je
    /// přihlášený teď (oznámení předchozího účtu by na cizím telefonu nemělo co dělat).
    /// </summary>
    public void Renew(string customerId, string p256dh, string auth, string deviceLabel)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Oznámení push potřebuje zákazníka.");
        }

        CustomerId = customerId;
        SetKeys(p256dh, auth);
        DeviceLabel = Truncate(deviceLabel);
    }

    private void SetKeys(string p256dh, string auth)
    {
        P256dh = ValidKey(p256dh);
        Auth = ValidKey(auth);
    }

    private static string ValidEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > MaxEndpointLength
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DomainException("Neplatná adresa pro oznámení push.");
        }

        return endpoint;
    }

    private static string ValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength
            || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '=' or '+' or '/'))
        {
            throw new DomainException("Neplatný klíč pro oznámení push.");
        }

        return key;
    }

    private static string Truncate(string label) =>
        string.IsNullOrWhiteSpace(label) ? "Neznámé zařízení"
            : label.Length <= MaxDeviceLabelLength ? label : label[..MaxDeviceLabelLength];
}
