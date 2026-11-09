namespace MiniShop.Infrastructure.Payments.Stripe;

/// <summary>
/// E-shop krok 16: přístup ke Stripu (konfigurace <c>Payments:Stripe:*</c>, produkce secrets <c>STRIPE_SECRET_KEY</c>
/// a <c>STRIPE_WEBHOOK_SECRET</c>). Ověřuje se hned při startu (fail fast) – se Stripem bez klíčů by žádná platba neprošla.
/// </summary>
/// <remarks>
/// Jen <b>testovací režim</b>: živý klíč (<c>sk_live_…</c>) Web odmítne – MiniShop je výukový projekt a skutečné peníze
/// přijímat nesmí.
/// </remarks>
public sealed record StripeSettings
{
    public const string TestKeyPrefix = "sk_test_";
    public const string WebhookSecretPrefix = "whsec_";

    public StripeSettings(string? secretKey, string? webhookSecret, string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(secretKey) || !secretKey.StartsWith(TestKeyPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Payments:Stripe:SecretKey musí být testovací klíč Stripu ({TestKeyPrefix}…) – živý klíč MiniShop nepřijme.");
        }

        if (string.IsNullOrWhiteSpace(webhookSecret) || !webhookSecret.StartsWith(WebhookSecretPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Chybí Payments:Stripe:WebhookSecret ({WebhookSecretPrefix}…) – bez něj nejde ověřit webhook Stripu.");
        }

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                "Chybí Notifications:PublicBaseUrl – Stripe potřebuje absolutní adresu, kam vrátit zákazníka po platbě.");
        }

        SecretKey = secretKey;
        WebhookSecret = webhookSecret;
        PublicBaseUrl = baseUrl.ToString().TrimEnd('/');
    }

    /// <summary>
    /// Nastavení Stripu podle <c>Payments:Gateway</c>: <c>Stripe</c> = načíst a ověřit klíče, <c>Fake</c> nebo nic = null
    /// (simulovaná brána). Jiná hodnota = chyba při startu (překlep by tiše zapnul simulaci).
    /// </summary>
    public static StripeSettings? FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var gateway = configuration["Payments:Gateway"];
        if (string.IsNullOrWhiteSpace(gateway) || gateway == Application.Payments.PaymentGatewayInfo.Fake)
        {
            return null;
        }

        if (gateway != Application.Payments.PaymentGatewayInfo.Stripe)
        {
            throw new InvalidOperationException($"Neznámá platební brána Payments:Gateway = „{gateway}“ (Fake nebo Stripe).");
        }

        return new StripeSettings(configuration["Payments:Stripe:SecretKey"], configuration["Payments:Stripe:WebhookSecret"],
            configuration["Notifications:PublicBaseUrl"]);
    }

    public string SecretKey { get; }

    public string WebhookSecret { get; }

    /// <summary>Adresa webu bez lomítka na konci (návrat ze Stripu na detail objednávky).</summary>
    public string PublicBaseUrl { get; }

    // Klíče do logu ani do výjimek nikdy – record by je jinak vypsal v ToString().
    public override string ToString() => $"StripeSettings {{ PublicBaseUrl = {PublicBaseUrl} }}";
}
