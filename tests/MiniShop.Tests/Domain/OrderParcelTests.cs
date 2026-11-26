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

    /// <summary>
    /// E-shop krok 28D: jeden ze dvou balíků se vrátí, druhý dorazí – objednávka je doručená a vrácený balík má automatickou
    /// vratku po částech s jeho položkami (sága začne naskladněním – událost OrderParcelReturnedToSender).
    /// </summary>
    [Fact]
    public void JedenBalikVracen_DruhyDorucen_ObjednavkaDorucena_VracenyMaVratkuPoCastech()
    {
        var order = ShippedSplitOrder();

        Assert.True(order.ReturnParcelToSender("CS-2", Now.AddHours(1)));
        Assert.False(order.ReturnParcelToSender("CS-2", Now.AddHours(1)));   // opakovaná zpráva
        Assert.Equal(OrderStatus.Shipped, order.Status);                      // balík 1 ještě jede
        var orderReturn = Assert.Single(order.Returns);
        Assert.Equal([(2, 3)], orderReturn.Lines.Select(l => (l.ProductId, l.Quantity)));
        Assert.Contains(order.DomainEvents, e => e is MiniShop.Domain.Orders.Events.OrderParcelReturnedToSender { ReturnNumber: 1 });

        Assert.True(order.MarkParcelDelivered("CS-1", Now.AddHours(2)));

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.DoesNotContain(order.DomainEvents, e => e is MiniShop.Domain.Orders.Events.OrderReturnedToSender);
    }

    /// <summary>Objednávka kurýrem se dvěma balíky (Praha: produkt 1, Brno: produkt 2 × 3), oba u dopravce.</summary>
    private static Order ShippedSplitOrder()
    {
        var order = OrderWith((1, 1), (2, 3));
        order.SetShipping(ShippingMethod.Courier, 99m, new DeliveryAddress("Dlouhá 5", "Praha", "11000"));
        order.Confirm(Now);
        order.SetParcels([new ParcelPlan("PRG", "Praha", [new(1, 1)]), new ParcelPlan("BRQ", "Brno", [new(2, 3)])]);
        order.StartPacking(Now);
        order.RequestPendingParcelShipments(Now);
        Assert.True(order.ConfirmParcelShipment(1, "PPL", "CS-2", Now));   // balík 1 = Brno (BRQ)
        Assert.True(order.ConfirmParcelShipment(2, "PPL", "CS-1", Now));
        Assert.Equal(OrderStatus.Shipped, order.Status);
        return order;
    }
}
