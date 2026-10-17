using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Pricing;

/// <summary>
/// Pravidlo pro výpočet slevy na položku. Nové pravidlo = nová třída, OrderService se nemění (Open/Closed).
/// </summary>
public interface IDiscountPolicy
{
    /// <returns>Sleva v Kč pro celou položku (0 = bez slevy).</returns>
    decimal CalculateDiscount(CatalogProduct product, int quantity);
}
