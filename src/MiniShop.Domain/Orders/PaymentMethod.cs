namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 8: způsob platby z pokladny. Ukládá se jako text.
/// </summary>
public enum PaymentMethod
{
    /// <summary>Objednávka bez pokladny (API, starší objednávky) – sága platbu nevyžaduje.</summary>
    None,

    /// <summary>Kartou přes platební bránu – po rezervaci zboží čeká objednávka na platbu.</summary>
    Card,

    /// <summary>Dobírka – platí se při převzetí, sága platbu přeskočí.</summary>
    CashOnDelivery
}
