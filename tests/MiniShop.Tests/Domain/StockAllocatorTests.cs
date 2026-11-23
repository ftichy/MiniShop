using MiniShop.Domain.Inventory;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 28A: přidělení zásob – jeden sklad, když to jde; jinak co nejméně skladů.</summary>
public sealed class StockAllocatorTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Category Peripherals = new("Periferie");

    /// <summary>Produkt se zásobou v Praze a v Brně.</summary>
    private static Product Product(string name, int prague, int brno)
    {
        var product = new Product(name, null, 100m, Peripherals, prague, Now);
        if (brno > 0)
        {
            product.AddStock(Warehouse.BrnoId, brno, Now);
        }

        return product;
    }

    [Fact]
    public void JedenSklad_MaVsechno_VezmeSeJenZNej()
    {
        var mouse = Product("Myš", prague: 1, brno: 5);
        var cable = Product("Kabel", prague: 0, brno: 5);

        var result = StockAllocator.Allocate([(mouse, 2), (cable, 1)])!;

        Assert.All(result, a => Assert.Equal(Warehouse.BrnoId, a.WarehouseId));   // Praha by nestačila – Brno má všechno
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ObaSkladyMajiVsechno_VyhrajeNizsiId()
    {
        var mouse = Product("Myš", prague: 5, brno: 5);

        var result = StockAllocator.Allocate([(mouse, 2)])!;

        Assert.Equal(Warehouse.DefaultId, Assert.Single(result).WarehouseId);
    }

    [Fact]
    public void ZadnySkladNemaVsechno_RozdeliSe_NejvicKusuNejdriv()
    {
        var mouse = Product("Myš", prague: 3, brno: 0);
        var cable = Product("Kabel", prague: 1, brno: 4);

        var result = StockAllocator.Allocate([(mouse, 3), (cable, 4)])!;

        // Praha pokryje 3 myši + 1 kabel = 4 ks, Brno 4 kabely = 4 ks → shoda, nejdřív Praha (nižší ID); zbylé 3 kabely z Brna.
        Assert.Equal(7, result.Sum(a => a.Quantity));
        Assert.Equal(2, result.Select(a => a.WarehouseId).Distinct().Count());
        Assert.Equal(3, result.Where(a => a.WarehouseId == Warehouse.BrnoId).Sum(a => a.Quantity));
    }

    [Fact]
    public void NestaciAniDohromady_Null()
    {
        var mouse = Product("Myš", prague: 1, brno: 1);

        Assert.Null(StockAllocator.Allocate([(mouse, 3)]));
    }

    [Fact]
    public void ZasobaPoSkladech_SoucetJeCelkovaZasoba_VraceniDoSkladuPuvodu()
    {
        var mouse = Product("Myš", prague: 2, brno: 3);

        mouse.RemoveStock(Warehouse.BrnoId, 3, Now);
        Assert.Equal(2, mouse.StockQuantity);
        Assert.Equal(0, mouse.StockIn(Warehouse.BrnoId));

        mouse.AddStock(Warehouse.BrnoId, 1, Now);   // uvolnění rezervace z Brna
        Assert.Equal((2, 1, 3), (mouse.StockIn(Warehouse.DefaultId), mouse.StockIn(Warehouse.BrnoId), mouse.StockQuantity));
    }

    [Fact]
    public void ZeSkladuNejdeVzitVicNezMa()
    {
        var mouse = Product("Myš", prague: 5, brno: 1);

        Assert.Throws<MiniShop.Domain.Exceptions.InsufficientStockException>(() => mouse.RemoveStock(Warehouse.BrnoId, 2, Now));
    }

    /// <summary>
    /// Postupné nasazení: starší verze skladu chvíli mění jen celkovou zásobu (sloupec StockQuantity). Rozdíl patří Praze
    /// a při další změně se do ní zapíše – součet skladů pak zase sedí.
    /// </summary>
    [Fact]
    public void RozdilZeStarsiVerze_PatriPrazeAPriZmeneSeZapise()
    {
        var mouse = Product("Myš", prague: 5, brno: 2);
        typeof(Product).GetProperty(nameof(MiniShop.Domain.Inventory.Product.StockQuantity))!.SetValue(mouse, 4);   // starší verze odečetla 3

        Assert.Equal((2, 2), (mouse.StockIn(Warehouse.DefaultId), mouse.StockIn(Warehouse.BrnoId)));

        mouse.AddStock(Warehouse.BrnoId, 1, Now);

        Assert.Equal((2, 3, 5), (mouse.StockIn(Warehouse.DefaultId), mouse.StockIn(Warehouse.BrnoId), mouse.StockQuantity));
        Assert.Equal(mouse.StockQuantity, mouse.Stocks.Sum(s => s.Quantity));
    }
}
