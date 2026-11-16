using MiniShop.Application.Payments;
using MiniShop.Domain.Payments;
using Stripe;
using Stripe.Checkout;

namespace MiniShop.Infrastructure.Payments.Stripe;

/// <summary>
/// E-shop krok 16: skutečná platební brána – <b>Stripe Checkout</b> v testovacím režimu. Adaptér: modul Payments zná jen
/// <see cref="IPaymentGateway"/>, o Stripu ví jen tahle třída a <see cref="StripeWebhookProcessor"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Platba = Checkout Session: stránka Stripu, kam zákazníka pošleme; číslo karty MiniShop vůbec nevidí.</item>
/// <item>Idempotency key: opakovaný příkaz (stejné <c>requestId</c>) vrátí tutéž session, refund téže platby jen jednou.
/// Stripe porovnává i parametry – proto v požadavku nic, co se při opakování mění (ID naší platby, „teď“).</item>
/// <item><c>order_id</c> v metadatech: podle něj webhook najde naši platbu (a ověří ID session).</item>
/// </list>
/// </remarks>
public sealed class StripePaymentGateway(IStripeClient stripe, StripeSettings settings) : IPaymentGateway
{
    public const string OrderIdMetadataKey = "order_id";

    /// <summary>
    /// Stripe chce konec session 30 min až 24 h od založení. Náš termín (<see cref="Payment.ExpiresAtUtc"/>) bývá
    /// kratší, proto session vyprší o tuhle dobu později – kdo zaplatí po timeoutu ságy, tomu peníze vrátí kompenzace
    /// (pozdní platba → <c>RefundPaymentV1</c>). Stránka /Pay po našem termínu na Stripe už nepustí.
    /// </summary>
    public static readonly TimeSpan SessionExpiryExtension = TimeSpan.FromMinutes(30);

    public async Task<GatewayPayment> CreateAsync(Payment payment, Guid requestId, CancellationToken cancellationToken = default)
    {
        var orderUrl = $"{settings.PublicBaseUrl}/Orders/{payment.OrderId}";
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            Locale = "cs",
            ClientReferenceId = payment.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Metadata = new Dictionary<string, string> { [OrderIdMetadataKey] = payment.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "czk",
                        UnitAmount = ToMinorUnits(payment.Amount),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = $"MiniShop – objednávka č. {payment.OrderId}" }
                    }
                }
            ],
            SuccessUrl = orderUrl,
            CancelUrl = orderUrl,
            ExpiresAt = payment.ExpiresAtUtc + SessionExpiryExtension
        };

        var session = await new SessionService(stripe).CreateAsync(options,
            new RequestOptions { IdempotencyKey = $"minishop-payment-{requestId:N}" }, cancellationToken);
        return new GatewayPayment(session.Id, session.Url);
    }

    public async Task RefundAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        // Bez částky Stripe vrátí zbytek PaymentIntentu – po částečných vratkách (e-shop krok 22) jen to, co ještě nevrátil.
        await new RefundService(stripe).CreateAsync(
            new RefundCreateOptions { PaymentIntent = await PaymentIntentIdAsync(payment, cancellationToken) },
            new RequestOptions { IdempotencyKey = $"minishop-refund-{payment.Id:N}" },
            cancellationToken);
    }

    /// <summary>
    /// E-shop krok 22: vrácení části (jedna vratka). Klíč idempotence = platba + číslo vratky – stejná vratka podruhé
    /// peníze nevrátí, další vratka téže platby má vlastní klíč.
    /// </summary>
    public async Task RefundPartAsync(Payment payment, PaymentRefund refund, CancellationToken cancellationToken = default)
    {
        await new RefundService(stripe).CreateAsync(
            new RefundCreateOptions
            {
                PaymentIntent = await PaymentIntentIdAsync(payment, cancellationToken),
                Amount = ToMinorUnits(refund.Amount)
            },
            new RequestOptions { IdempotencyKey = $"minishop-refund-{payment.Id:N}-return-{refund.ReturnNumber}" },
            cancellationToken);
    }

    /// <summary>Peníze drží PaymentIntent session – zjistíme ho až při vrácení (neukládáme ho, tabulka plateb se tak nemění).</summary>
    private async Task<string> PaymentIntentIdAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(payment.ExternalId))
        {
            throw new InvalidOperationException($"Platba objednávky {payment.OrderId} nemá ID session u Stripu.");
        }

        var session = await new SessionService(stripe).GetAsync(payment.ExternalId, cancellationToken: cancellationToken);
        if (string.IsNullOrEmpty(session.PaymentIntentId))
        {
            throw new InvalidOperationException($"Session {session.Id} (objednávka {payment.OrderId}) nemá platbu k vrácení.");
        }

        return session.PaymentIntentId;
    }

    /// <summary>Stripe počítá v nejmenší jednotce měny – u korun v haléřích (199,90 Kč = 19990).</summary>
    public static long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
