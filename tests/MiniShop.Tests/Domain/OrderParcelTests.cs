using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 28B: balíky objednávky – jen jednou, číslované, bez rozdělení jeden balík z Prahy.</summary>
public sealed class OrderParcelTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

    private static Order OrderWith(params (int ProductId, int Quantity)[] items)
    {
        var order = new Order("zakaznik-1", "Jan Novák", "jan@example.com", Now);
        foreach (var (productId, quantity) in items)
        {
            var product = new CatalogProduct(productId, $"Produkt {productId}", 100m, Now);
            product.ApplyAvailability(CatalogAvailability.InStock, Now);
            order.AddItem(product, quantity, 0m);
        }

        return order;
    }

    [Fact]
    public void BezRozdeleni_JedenBalikZPrahySeVsemiPolozkami()
    {
        var order = OrderWith((1, 2), (2, 1));

        var parcel = Assert.Single(order.EffectiveParcels());

        Assert.Equal((1, "PRG", "Praha"), (parcel.Number, parcel.WarehouseCode, parcel.WarehouseName));
        Assert.Equal([new OrderParcelLine(1, 2), new OrderParcelLine(2, 1)], parcel.Lines);
        Assert.Empty(order.Parcels);   // jen pro zobrazení – nic se neukládá
    }

    [Fact]
    public void RozdeleniOdSkladu_CislujePodleKoduSkladu_JenJednou()
    {
        var order = OrderWith((1, 4));

        Assert.True(order.SetParcels([
            new ParcelPlan("PRG", "Praha", [new(1, 1)]),
            new ParcelPlan("BRQ", "Brno", [new(1, 3)])]));
        Assert.False(order.SetParcels([new ParcelPlan("PRG", "Praha", [new(1, 4)])]));   // opakovaná zpráva

        Assert.Equal([(1, "BRQ", 3), (2, "PRG", 1)],
            order.EffectiveParcels().Select(p => (p.Number, p.WarehouseCode, p.Lines.Sum(l => l.Quantity))));
    }
}
