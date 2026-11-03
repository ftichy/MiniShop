namespace MiniShop.Application.Payments;

/// <summary>
/// E-shop krok 8: sdílené tajemství s platební bránou (konfigurace <c>Payments:WebhookSecret</c>, produkce secret
/// <c>PAYMENTS_WEBHOOK_SECRET</c>). Podle něj ověřujeme, že webhook opravdu poslala brána.
/// </summary>
public sealed record PaymentsSettings
{
    public const int MinSecretLength = 32;

    public PaymentsSettings(string? webhookSecret)
    {
        // Fail fast jako servisní klíč skladu: bez tajemství by kdokoli „zaplatil“ cizí objednávku.
        if (string.IsNullOrWhiteSpace(webhookSecret) || webhookSecret.Length < MinSecretLength)
        {
            throw new InvalidOperationException(
                $"Chybí Payments:WebhookSecret (aspoň {MinSecretLength} znaků) – bez něj nejde ověřit webhook platební brány.");
        }

        WebhookSecret = webhookSecret;
    }

    public string WebhookSecret { get; }
}
