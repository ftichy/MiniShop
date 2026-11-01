using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// E-shop krok 2: doména skladu → integrační kontrakty. Na jednom místě, protože zprávu o produktu skládá
/// InventoryDbContext (při uložení změny) i SyncCatalogHandler (obnova katalogu).
/// </summary>
public static class InventoryContractMapping
{
    /// <summary>Celý stav produktu pro výlohu. Kategorie musí být načtená (repozitář ji načítá vždy).</summary>
    public static ProductUpdatedV2 ToProductUpdatedV2(this Product product, Guid eventId, DateTime occurredAtUtc) => new(
        eventId, occurredAtUtc, product.Id, product.Name, product.Description, product.Price,
        product.CategoryId, product.Category.Name, product.ImageFileName,
        product.Availability.ToContract(), product.AvailabilityChangedAtUtc);

    /// <summary>Výčet domény → text kontraktu. Výslovně, ne ToString(): přejmenování v doméně nesmí změnit kontrakt.</summary>
    public static string ToContract(this StockAvailability availability) => availability switch
    {
        StockAvailability.InStock => StockAvailabilityV1.InStock,
        StockAvailability.LowStock => StockAvailabilityV1.LowStock,
        StockAvailability.OutOfStock => StockAvailabilityV1.OutOfStock,
        _ => throw new ArgumentOutOfRangeException(nameof(availability))
    };
}
