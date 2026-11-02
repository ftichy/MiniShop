using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Pricing;

/// <summary>
/// E-shop krok 4: osobní odběr zdarma, kurýr za pevnou cenu, od zadané částky zdarma.
/// </summary>
public sealed class StandardShippingPriceCalculator : IShippingPriceCalculator
{
    private readonly decimal _courierPrice;
    private readonly decimal _freeFrom;

    public StandardShippingPriceCalculator(decimal courierPrice, decimal freeFrom)
    {
        if (courierPrice < 0) throw new ArgumentOutOfRangeException(nameof(courierPrice));
        if (freeFrom < 0) throw new ArgumentOutOfRangeException(nameof(freeFrom));

        _courierPrice = courierPrice;
        _freeFrom = freeFrom;
    }

    public decimal Calculate(ShippingMethod method, decimal itemsTotal) => method switch
    {
        ShippingMethod.Courier => itemsTotal >= _freeFrom ? 0m : _courierPrice,
        _ => 0m
    };
}
