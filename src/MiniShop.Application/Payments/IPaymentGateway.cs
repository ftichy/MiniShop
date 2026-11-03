using MiniShop.Domain.Payments;

namespace MiniShop.Application.Payments;

/// <summary>
/// E-shop krok 8: platební brána (cizí systém). Založí u sebe platbu a řekne, kam poslat zákazníka zaplatit.
/// Výsledek pošle později sama – podepsaným webhookem na /api/payments/webhook (<see cref="PaymentWebhookProcessor"/>).
/// </summary>
/// <remarks>
/// Dependency Inversion: modul zná jen tohle rozhraní. Dnes simulace (FakePaymentGateway), skutečná brána
/// (Stripe v testovacím režimu, sandbox GoPay/Comgate) = jen jiná implementace.
/// </remarks>
public interface IPaymentGateway
{
    Task<GatewayPayment> CreateAsync(Payment payment, CancellationToken cancellationToken = default);
}

/// <param name="ExternalId">ID platby u brány.</param>
/// <param name="CheckoutUrl">Stránka brány, kam poslat zákazníka.</param>
public sealed record GatewayPayment(string ExternalId, string CheckoutUrl);
