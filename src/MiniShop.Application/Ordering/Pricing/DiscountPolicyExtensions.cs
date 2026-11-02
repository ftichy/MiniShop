using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Pricing;

/// <summary>E-shop krok 4: výpočet slevy sdílený objednávkou přes API a pokladnou.</summary>
public static class DiscountPolicyExtensions
{
    /// <summary>Uplatní se nejvýhodnější sleva ze všech pravidel (slevy se nesčítají).</summary>
    public static decimal BestDiscount(this IEnumerable<IDiscountPolicy> policies, CatalogProduct product, int quantity) =>
        policies
            .Select(policy => policy.CalculateDiscount(product, quantity))
            .DefaultIfEmpty(0m)
            .Max();
}
