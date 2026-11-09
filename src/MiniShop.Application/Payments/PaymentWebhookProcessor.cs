using System.Text.Json;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Payments;

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

        return payload.Status switch
        {
            PaymentWebhookPayload.Succeeded => await ApplyResultAsync(payment, failureReason: null, cancellationToken),
            PaymentWebhookPayload.Failed => await ApplyResultAsync(payment, payload.Reason ?? "Platba byla zamítnuta.", cancellationToken),
            _ => PaymentWebhookResult.Invalid
        };
    }

    /// <summary>
    /// Výsledek platby z ověřeného webhooku (simulace i Stripe – e-shop krok 16): <paramref name="failureReason"/> null =
    /// zaplaceno, jinak nezaplaceno s důvodem. Opakovaný výsledek nic nezmění.
    /// </summary>
    public async Task<PaymentWebhookResult> ApplyResultAsync(Payment payment, string? failureReason, CancellationToken cancellationToken = default)
    {
        TraceTags.TagOrder(payment.OrderId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var changed = failureReason is null ? payment.Succeed(now) : payment.Fail(failureReason, now);

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
