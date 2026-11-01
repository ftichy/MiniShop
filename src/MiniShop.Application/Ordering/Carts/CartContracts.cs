namespace MiniShop.Application.Ordering.Carts;

/// <summary>
/// E-shop krok 3: košík pro zobrazení – řádky doplněné o aktuální údaje z kopie katalogu.
/// </summary>
/// <param name="Total">Orientační součet řádků, které jdou objednat (bez slev – ty spočítá až pokladna).</param>
public sealed record CartDto(IReadOnlyList<CartLineDto> Lines, decimal Total, int TotalQuantity)
{
    public static CartDto Empty { get; } = new([], 0m, 0);

    public bool IsEmpty => Lines.Count == 0;
}

/// <param name="UnitPrice">Aktuální cena z kopie katalogu.</param>
/// <param name="PriceWhenAdded">Cena při přidání – liší-li se, UI upozorní na změnu.</param>
/// <param name="Availability">Dostupnost z kopie katalogu (jako <see cref="CatalogItemDto.Availability"/>).</param>
/// <param name="IsInOffer">false = produkt sklad odebral z nabídky; řádek jde jen odebrat.</param>
public sealed record CartLineDto(
    int ProductId,
    string Name,
    string? ImageFileName,
    int Quantity,
    decimal UnitPrice,
    decimal PriceWhenAdded,
    string Availability,
    bool IsInOffer)
{
    public decimal LineTotal => UnitPrice * Quantity;

    public bool PriceChanged => IsInOffer && UnitPrice != PriceWhenAdded;

    /// <summary>Půjde objednat? Vyprodaný řádek v košíku zůstane (zboží se může vrátit), ale do součtu se nepočítá.</summary>
    public bool IsOrderable => IsInOffer && Availability != "OutOfStock";
}
