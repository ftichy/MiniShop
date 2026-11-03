using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Payments;
using MiniShop.Domain.Payments;

namespace MiniShop.Infrastructure.Payments;

public sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

    public Task<Payment?> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public void Add(Payment payment) => db.Payments.Add(payment);
}
