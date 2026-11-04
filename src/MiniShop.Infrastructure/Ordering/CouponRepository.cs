using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering.Coupons;
using MiniShop.Domain.Orders.Coupons;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>E-shop krok 13: slevové kódy.</summary>
public sealed class CouponRepository(OrderingDbContext db) : ICouponRepository
{
    public Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.Coupons.FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

    public Task<Coupon?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.Coupons.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Coupon>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Coupons.AsNoTracking().OrderByDescending(c => c.Id).ToListAsync(cancellationToken);

    public void Add(Coupon coupon) => db.Coupons.Add(coupon);
}
