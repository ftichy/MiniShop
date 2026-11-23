using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Inventory;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 28A (směr C): dva sklady. Rezervace vezme zboží z jednoho skladu, když to jde, jinak ho rozdělí;
/// uvolnění vrátí kusy tam, odkud odešly. Ordering a zákazník o skladech nevědí – objednávka se potvrdí stejně.
/// </summary>
public sealed partial class OrderServiceTests
{
    [Fact]
    public async Task Sklady_RezervaceZeDvouSkladu_ZruseniVratiKusyDoSkladuPuvodu()
    {
        var product = await AddProductAsync(price: 100m, stock: 2);                       // Praha 2
        await Products(s => s.AddStockAsync(product.Id, 3, Warehouse.BrnoId));           // Brno 3
        await ProcessOutboxAsync();

        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 4);             // ani jeden sklad nemá 4 → rozdělit

        var reserved = await InventoryDb(db => db.StockReservations.Where(r => r.OrderId == order.Id)
            .Select(r => new { r.WarehouseId, r.Quantity }).ToListAsync());
        Assert.Equal([(Warehouse.DefaultId, 1), (Warehouse.BrnoId, 3)],   // nejdřív sklad, který pokryje víc (Brno 3), zbytek z Prahy
            reserved.OrderBy(r => r.WarehouseId).Select(r => (r.WarehouseId, r.Quantity)));
        Assert.Equal(1, (await Products(s => s.GetByIdAsync(product.Id))).StockQuantity);

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));
        await ProcessOutboxAsync();   // sága → ReleaseStockV1 → zpět do skladů původu

        var after = await Products(s => s.GetByIdAsync(product.Id));
        Assert.Equal(5, after.StockQuantity);
        Assert.Equal([(Warehouse.DefaultId, 2), (Warehouse.BrnoId, 3)], after.Stocks!.Select(s => (s.WarehouseId, s.Quantity)));
    }

    [Fact]
    public async Task Sklady_NaskladneniDoNeexistujicihoSkladu_Chyba()
    {
        var product = await AddProductAsync(price: 100m, stock: 2);

        await Assert.ThrowsAsync<MiniShop.Domain.Exceptions.DomainException>(() => Products(s => s.AddStockAsync(product.Id, 1, 99)));
    }
}
