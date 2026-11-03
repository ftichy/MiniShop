using System.Text.Json;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Application.Payments;

/// <summary>
/// E-shop krok 8: zpracování webhooku platební brány – výsledek platby. Volá ho HTTP endpoint /api/payments/webhook
/// i simulace brány (stejná cesta, stejná kontrola podpisu).
/// </summary>
/// <remarks>
/// Pořadí je důležité: nejdřív podpis (nic nečíst z nepodepsané zprávy), pak teprve obsah.
/// Opakovaný webhook (brána nedostala odpověď) je v pořádku – platba je stavový automat a podruhé nic neudělá.
/// Výsledek jde z modulu ven přes outbox (PaymentSucceededV1 / PaymentFailedV1) v téže transakci jako změna platby.
/// </remarks>
public sealed class PaymentWebhookProcessor(
    PaymentsSettings settings,
    IPaymentRepository payments,
    IPaymentsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PaymentWebhookResult> ProcessAsync(string body, string? signature, CancellationToken cancellationToken = default)
    {
        if (!WebhookSignature.IsValid(settings.WebhookSecret, body, signature))
        {
            return PaymentWebhookResult.InvalidSignature;
        }

        PaymentWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PaymentWebhookPayload>(body, Json);
        }
        catch (JsonException)
        {
            return PaymentWebhookResult.Invalid;
        }

        if (payload is null || payload.PaymentId == Guid.Empty)
        {
            return PaymentWebhookResult.Invalid;
        }

        var payment = await payments.GetAsync(payload.PaymentId, cancellationToken);
        if (payment is null)
        {
            return PaymentWebhookResult.UnknownPayment;
        }

        TraceTags.TagOrder(payment.OrderId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var changed = payload.Status switch
            {
                PaymentWebhookPayload.Succeeded => payment.Succeed(now),
                PaymentWebhookPayload.Failed => payment.Fail(payload.Reason ?? "Platba byla zamítnuta.", now),
                _ => throw new DomainException($"Neznámý výsledek platby „{payload.Status}“.")
            };

            if (changed)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);   // → outbox: PaymentSucceededV1 / PaymentFailedV1
            }

            return PaymentWebhookResult.Accepted;
        }
        catch (DomainException)
        {
            // Rozporný výsledek (platba už zaplacená a teď „zamítnuto“) – nic neměníme, bráně odpovíme chybou.
            return PaymentWebhookResult.Invalid;
        }
    }
}

/// <summary>Tělo webhooku (JSON). <see cref="Status"/> = <c>succeeded</c> / <c>failed</c>.</summary>
public sealed record PaymentWebhookPayload(Guid PaymentId, string Status, string? Reason = null)
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public enum PaymentWebhookResult
{
    /// <summary>Zpracováno (i opakovaný webhook) → 200.</summary>
    Accepted,

    /// <summary>Špatný nebo chybějící podpis → 401.</summary>
    InvalidSignature,

    /// <summary>Nečitelné tělo nebo rozporný výsledek → 400.</summary>
    Invalid,

    /// <summary>Platbu neznáme → 404.</summary>
    UnknownPayment
}
