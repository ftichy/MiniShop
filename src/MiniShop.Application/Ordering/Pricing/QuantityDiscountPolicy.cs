using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Pricing;

/// <summary>
/// Množstevní sleva: od zadaného počtu kusů se zlevňuje o daný podíl (např. 0,10 = 10 %).
/// </summary>
public sealed class QuantityDiscountPolicy : IDiscountPolicy
{
    private readonly int _minimumQuantity;
    private readonly decimal _percentage;

    public QuantityDiscountPolicy(int minimumQuantity, decimal percentage)
    {
        if (minimumQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(minimumQuantity));
        if (percentage is <= 0m or >= 1m) throw new ArgumentOutOfRangeException(nameof(percentage));

        _minimumQuantity = minimumQuantity;
        _percentage = percentage;
    }

    public decimal CalculateDiscount(CatalogProduct product, int quantity) =>
        quantity >= _minimumQuantity
            ? Math.Round(product.Price * quantity * _percentage, 2)
            : 0m;
}
