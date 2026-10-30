namespace MiniShop.Domain.Inventory;

/// <summary>
/// E-shop krok 2: hrubé pásmo dostupnosti pro výlohu. Zákazník nepotřebuje přesné číslo – to patří skladu.
/// </summary>
public enum StockAvailability
{
    /// <summary>Víc než <see cref="StockAvailabilityBands.LowStockLimit"/> kusů.</summary>
    InStock,

    /// <summary>1 až <see cref="StockAvailabilityBands.LowStockLimit"/> kusů.</summary>
    LowStock,

    OutOfStock
}

public static class StockAvailabilityBands
{
    /// <summary>Do kolika kusů včetně jsou to „poslední kusy“.</summary>
    public const int LowStockLimit = 5;

    public static StockAvailability From(int stockQuantity) => stockQuantity switch
    {
        <= 0 => StockAvailability.OutOfStock,
        <= LowStockLimit => StockAvailability.LowStock,
        _ => StockAvailability.InStock
    };
}
