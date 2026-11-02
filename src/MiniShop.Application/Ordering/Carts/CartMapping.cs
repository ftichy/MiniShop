using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Application.Ordering.Carts;

/// <summary>Košík pro zobrazení – sdílí ho stránka Košík i pokladna (e-shop krok 4).</summary>
internal static class CartMapping
{
    /// <summary>Produkty košíku z kopie katalogu – i odebrané (řádek pak ukáže „už není v nabídce“ s názvem).</summary>
    public static async Task<IReadOnlyDictionary<int, CatalogProduct>> GetForCartAsync(
        this ICatalogProductRepository catalog, Cart cart, CancellationToken cancellationToken) =>
        (await catalog.GetByIdsAsync(cart.Lines.Select(l => l.ProductId), cancellationToken)).ToDictionary(p => p.Id);

    /// <param name="catalog">Produkty košíku z kopie katalogu (i stažené).</param>
    public static CartDto ToDto(this Cart cart, IReadOnlyDictionary<int, CatalogProduct> catalog)
    {
        // V pořadí přidání (nový produkt na konec), ne podle názvu – řádky při změně množství neposkakují.
        var lines = cart.Lines
            .OrderBy(line => line.Id)
            .Select(line => ToDto(line, catalog.GetValueOrDefault(line.ProductId)))
            .ToList();

        return new CartDto(lines, lines.Where(l => l.IsOrderable).Sum(l => l.LineTotal), cart.TotalQuantity);
    }

    private static CartLineDto ToDto(CartLine line, CatalogProduct? product) =>
        product is null
            // Kopie katalogu produkt vůbec nezná (např. obnovená DB) – stejné jako odebraný.
            ? new(line.ProductId, $"Produkt č. {line.ProductId}", null, line.Quantity, line.PriceWhenAdded,
                line.PriceWhenAdded, CatalogAvailability.Unknown.ToString(), IsInOffer: false)
            : new(line.ProductId, product.Name, product.ImageFileName, line.Quantity, product.Price,
                line.PriceWhenAdded, product.Availability.ToString(), IsInOffer: !product.IsRemoved);
}
