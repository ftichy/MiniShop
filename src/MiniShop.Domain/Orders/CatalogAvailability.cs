namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 2: dostupnost produktu, jak ji zná kopie katalogu (podle zpráv skladu). Přesnou zásobu Ordering nezná.
/// </summary>
public enum CatalogAvailability
{
    /// <summary>Kopie zatím dostupnost nezná (produkt z doby před krokem 2) – po startu Webu se doplní obnovou katalogu.</summary>
    Unknown,
    InStock,
    LowStock,
    OutOfStock
}
