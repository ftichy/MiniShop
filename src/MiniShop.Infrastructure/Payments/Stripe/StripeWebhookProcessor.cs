using System.Globalization;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Payments;
using Stripe;
using Stripe.Checkout;

namespace MiniShop.Infrastructure.Payments.Stripe;

/// <summary>
/// E-shop krok 16: webhook Stripu (<c>/api/payments/stripe-webhook</c>). Ověří podpis, z události vytáhne objednávku
/// a výsledek předá společnému jádru <see cref="PaymentWebhookProcessor.ApplyResultAsync"/> (stejné jako u simulace).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Podpis: hlavička <c>Stripe-Signature</c> = čas + HMAC-SHA256 z „čas.tělo“ tajemstvím <c>whsec_…</c>.
/// Zpráva starší než 5 minut se odmítne (útočník nemůže přehrát starou zachycenou zprávu).</item>
/// <item>Stripe posílá události <b>celého účtu</b> – testovací účet sdílí vývoj i produkce. Platbu proto hledáme podle
/// <c>order_id</c> a navíc musí sedět ID session; cizí session = 200 bez efektu (jinak by Stripe zkoušel znovu
/// a nakonec endpoint vypnul).</item>
/// <item>Typy událostí, které nás nezajímají, = 200 bez efektu.</item>
/// </list>
/// </remarks>
public sealed class StripeWebhookProcessor(
    StripeSettings settings,
    IPaymentRepository payments,
    PaymentWebhookProcessor results,
    TimeProvider timeProvider,
    ILogger<StripeWebhookProcessor> logger)
{
    public const string SignatureHeaderName = "Stripe-Signature";

    /// <summary>Jak stará smí zpráva být (výchozí hodnota Stripu).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public async Task<PaymentWebhookResult> ProcessAsync(string body, string? signature, CancellationToken cancellationToken = default)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(body, signature ?? string.Empty, settings.WebhookSecret,
                (long)Tolerance.TotalSeconds, timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                throwOnApiVersionMismatch: false);   // verzi API určuje nastavení endpointu ve Stripu, ne knihovna
        }
        catch (StripeException)
        {
            // Chybějící/špatný podpis nebo příliš stará zpráva – nic z těla nečíst.
            return PaymentWebhookResult.InvalidSignature;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Podpis sedí, ale tělo nejde přečíst (jiný formát) – Stripu odpovíme 400.
            logger.LogWarning(exception, "Webhook Stripu s platným podpisem nejde přečíst");
            return PaymentWebhookResult.Invalid;
        }

        var failureReason = stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted or EventTypes.CheckoutSessionAsyncPaymentSucceeded => null,
            EventTypes.CheckoutSessionExpired => "Platba na bráně vypršela.",
            EventTypes.CheckoutSessionAsyncPaymentFailed => "Platba byla na bráně zamítnuta.",
            _ => "ignore"
        };
        if (failureReason == "ignore" || stripeEvent.Data.Object is not Session session)
        {
            return PaymentWebhookResult.Accepted;   // jiná událost účtu – nás se netýká
        }

        // checkout.session.completed u karty = zaplaceno; „unpaid“ by znamenalo odloženou platbu (přijde async událost).
        if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted && session.PaymentStatus != "paid")
        {
            return PaymentWebhookResult.Accepted;
        }

        if (!session.Metadata.TryGetValue(StripePaymentGateway.OrderIdMetadataKey, out var orderIdText)
            || !int.TryParse(orderIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var orderId))
        {
            return PaymentWebhookResult.Accepted;   // session nezaložil MiniShop
        }

        var payment = await payments.GetByOrderIdAsync(orderId, cancellationToken);
        if (payment is null || payment.ExternalId != session.Id)
        {
            // Session jiného prostředí (vývoj a produkce sdílí testovací účet) nebo starší pokus – nic neměnit.
            logger.LogInformation("Webhook Stripu {EventType} pro session, kterou tahle instance nezná (objednávka {OrderId}) – ignorováno",
                stripeEvent.Type, orderId);
            return PaymentWebhookResult.Accepted;
        }

        var result = await results.ApplyResultAsync(payment, failureReason, cancellationToken);

        // Rozporný výsledek (např. vypršení už zaplacené session) Stripu neoznamujeme chybou – opakování by nic nezměnilo.
        return result == PaymentWebhookResult.Invalid ? PaymentWebhookResult.Accepted : result;
    }
}
