namespace MiniShop.Web.Catalog;

/// <summary>E-shop krok 2: dostupnost z kopie katalogu (CatalogItemDto.Availability) pro zobrazení – výloha i formulář objednávky.</summary>
public static class AvailabilityLabels
{
    public static string Text(string availability) => availability switch
    {
        "InStock" => "Skladem",
        "LowStock" => "Poslední kusy",
        "OutOfStock" => "Vyprodáno",
        _ => "Dostupnost ověříme"
    };

    public static string Badge(string availability) => availability switch
    {
        "InStock" => "bg-success",
        "LowStock" => "bg-warning text-dark",
        "OutOfStock" => "bg-secondary",
        _ => "bg-light text-dark border"
    };
}
