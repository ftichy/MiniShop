using MiniShop.Domain.Products;

namespace MiniShop.Application.Pricing;

/// <summary>
/// Pravidlo pro výpočet slevy na položku. Nové pravidlo = nová třída, OrderService se nemění (Open/Closed).
/// </summary>
public interface IDiscountPolicy
{
    /// <returns>Sleva v Kč pro celou položku (0 = bez slevy).</returns>
    decimal CalculateDiscount(Product product, int quantity);
}
