using MiniShop.Domain.Orders.Coupons;

namespace MiniShop.Application.Ordering.Coupons;

/// <summary>E-shop krok 13: slevové kódy v ordering.db.</summary>
public interface ICouponRepository
{
    /// <summary>Kupon podle kódu (normalizovaného – <see cref="Coupon.NormalizeCode"/>), nebo null.</summary>
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<Coupon?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Všechny kupony, nejnovější první (stránka Kupony).</summary>
    Task<IReadOnlyList<Coupon>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(Coupon coupon);
}
