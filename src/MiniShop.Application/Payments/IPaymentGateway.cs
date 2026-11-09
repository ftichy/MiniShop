using MiniShop.Domain.Payments;

namespace MiniShop.Application.Payments;

/// <summary>
/// E-shop krok 8: platební brána (cizí systém). Založí u sebe platbu a řekne, kam poslat zákazníka zaplatit.
/// Výsledek pošle později sama – webhookem (simulace <c>/api/payments/webhook</c>, Stripe <c>/api/payments/stripe-webhook</c>).
/// </summary>
/// <remarks>
/// Dependency Inversion: modul zná jen tohle rozhraní. Implementace: simulace (FakePaymentGateway, krok 8)
/// a Stripe v testovacím režimu (StripePaymentGateway, e-shop krok 16) – sága ani Ordering rozdíl nepoznají.
/// </remarks>
public interface IPaymentGateway
{
    /// <summary>Založit platbu u brány.</summary>
    /// <param name="requestId">
    /// E-shop krok 16: ID požadavku pro bránu (idempotency key) – při opakování téhož příkazu stejné, takže brána
    /// vrátí tutéž platbu a nevznikne druhá (pád mezi voláním brány a uložením platby).
    /// </param>
    Task<GatewayPayment> CreateAsync(Payment payment, Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 16: vrátit peníze zaplacené platby. Opakované volání pro tutéž platbu nesmí vrátit peníze dvakrát
    /// (idempotence u brány). Simulace nedělá nic.
    /// </summary>
    Task RefundAsync(Payment payment, CancellationToken cancellationToken = default);
}

/// <param name="ExternalId">ID platby u brány.</param>
/// <param name="CheckoutUrl">Stránka brány, kam poslat zákazníka (simulace relativně <c>/Pay/…</c>, skutečná brána absolutně).</param>
public sealed record GatewayPayment(string ExternalId, string CheckoutUrl);

/// <summary>E-shop krok 16: kterou bránu Web používá (konfigurace <c>Payments:Gateway</c>) – stránky podle ní mění texty.</summary>
/// <param name="Name"><see cref="Fake"/> nebo <see cref="Stripe"/>.</param>
public sealed record PaymentGatewayInfo(string Name)
{
    public const string Fake = "Fake";
    public const string Stripe = "Stripe";

    /// <summary>Testovací karta Stripu – v testovacím režimu projde vždy.</summary>
    public const string StripeTestCard = "4242 4242 4242 4242";

    public bool IsStripe => Name == Stripe;
}
