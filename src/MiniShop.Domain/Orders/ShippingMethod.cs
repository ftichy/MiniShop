namespace MiniShop.Domain.Orders;

/// <summary>E-shop krok 4: způsob dopravy objednávky (ukládá se jako text – nové hodnoty jen na konec).</summary>
public enum ShippingMethod
{
    /// <summary>Doprava nezadaná – objednávky z doby před pokladnou a objednávky přes REST API.</summary>
    None,

    /// <summary>Osobní odběr – bez adresy.</summary>
    Pickup,

    /// <summary>Kurýr na doručovací adresu.</summary>
    Courier
}
