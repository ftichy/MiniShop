using MiniShop.Domain.Payments;

namespace MiniShop.Application.Payments;

/// <summary>E-shop krok 8: platby (jedna na objednávku).</summary>
public interface IPaymentRepository
{
    Task<Payment?> GetAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
    void Add(Payment payment);
}
