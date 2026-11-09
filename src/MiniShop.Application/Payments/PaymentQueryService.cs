using MiniShop.Application.Exceptions;

namespace MiniShop.Application.Payments;

/// <summary>E-shop krok 8: platba pro stránku brány – zákazník vidí jen svou (cizí = jako neexistující).</summary>
public sealed class PaymentQueryService(IPaymentRepository payments, TimeProvider timeProvider)
{
    /// <returns>Null = platba ještě nevznikla (sága teprve rezervuje zboží) – stránka počká.</returns>
    public async Task<PaymentDto?> GetForCustomerAsync(int orderId, string customerId, CancellationToken cancellationToken = default)
    {
        var payment = await payments.GetByOrderIdAsync(orderId, cancellationToken);
        if (payment is not null && payment.CustomerId != customerId)
        {
            throw new NotFoundException($"Platba objednávky {orderId} nebyla nalezena.");
        }

        return payment is null
            ? null
            : new PaymentDto(payment.Id, payment.OrderId, payment.Amount, payment.Status.ToString(), payment.ExpiresAtUtc,
                payment.IsExpired(timeProvider.GetUtcNow().UtcDateTime), payment.FailureReason, payment.CheckoutUrl);
    }
}

/// <param name="Status"><c>Pending</c>, <c>Succeeded</c>, <c>Failed</c>, <c>Refunded</c>.</param>
/// <param name="CheckoutUrl">Kam poslat zákazníka zaplatit (e-shop krok 16: u Stripu absolutní adresa brány).</param>
public sealed record PaymentDto(Guid PaymentId, int OrderId, decimal Amount, string Status, DateTime ExpiresAtUtc, bool IsExpired,
    string? FailureReason, string? CheckoutUrl = null);
