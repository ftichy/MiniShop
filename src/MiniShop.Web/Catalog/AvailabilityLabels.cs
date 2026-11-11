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
        "InStock" => "badge-soft-success",   // redesign A: jemné barvy (site.css)
        "LowStock" => "badge-soft-warning",
        "OutOfStock" => "badge-soft-secondary",
        _ => "badge-soft-secondary"
    };
}
