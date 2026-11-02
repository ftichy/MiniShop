using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Pricing;

/// <summary>
/// E-shop krok 4: cena dopravy (strategie). Jiný ceník (podle váhy, akce „doprava zdarma“) = nová implementace,
/// pokladna se nemění (Open/Closed).
/// </summary>
public interface IShippingPriceCalculator
{
    /// <param name="itemsTotal">Součet položek po slevách – od něj se odvíjí doprava zdarma.</param>
    decimal Calculate(ShippingMethod method, decimal itemsTotal);
}
