using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 22: vratka po částech na objednávce – kolik kusů jde vrátit, podíl slevy, jedna rozpracovaná vratka,
/// vratka celé objednávky jen jako první. Bez databáze a zpráv.
/// </summary>
public class OrderReturnTests
{
    private static readonly DateTime Start = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromDays(14);

    /// <summary>Doručená objednávka: 3× myš po 100 Kč se slevou 10 Kč (kupon), 1× klávesnice 500 Kč, kurýr 99 Kč.</summary>
    private static Order Delivered()
    {
        var order = new Order("zakaznik-1", "Jan", "jan@example.com", Start);
        order.AddItem(new CatalogProduct(7, "Myš", 100m, Start), quantity: 3, discount: 10m);
        order.AddItem(new CatalogProduct(8, "Klávesnice", 500m, Start), quantity: 1, discount: 0m);
        order.SetShipping(ShippingMethod.Courier, 99m, new DeliveryAddress("Dlouhá 5", "Praha", "11000"));
        order.Confirm(Start);
        order.StartPacking(Start);
        order.Ship("PPL", "123", Start);
        order.MarkDelivered(Start);
        order.ClearDomainEvents();
        return order;
    }

    private static Dictionary<int, int> Qty(params (int ProductId, int Quantity)[] lines) =>
        lines.ToDictionary(l => l.ProductId, l => l.Quantity);

    [Fact]
    public void Cast_VratkaSPodilemSlevy_ObjednavkaZustavaDorucena()
    {
        var order = Delivered();

        var number = order.RequestReturn("Nesedí", Qty((7, 1)), Start.AddDays(1), Window);

        Assert.Equal(1, number);
        Assert.Equal(OrderStatus.Completed, order.Status);
        var line = Assert.Single(order.GetReturn(1).Lines);
        Assert.Equal((1, 100m, 3.33m, 96.67m), (line.Quantity, line.UnitPrice, line.Discount, line.LineTotal));
        Assert.Equal(2, order.ReturnableQuantity(7));
        Assert.True(order.IsActive);   // rozpracovaná vratka blokuje smazání účtu
        Assert.Equal(1, Assert.IsType<OrderPartialReturnRequested>(Assert.Single(order.DomainEvents)).ReturnNumber);
    }

    [Fact]
    public void Cast_PosledniKusyDostanouZbytekSlevy_SoucetSedi()
    {
        var order = Delivered();
        order.RequestReturn("a", Qty((7, 1)), Start, Window);
        order.CompletePartialReturn(1, Start);
        order.RequestReturn("b", Qty((7, 1)), Start, Window);
        order.CompletePartialReturn(2, Start);
        order.RequestReturn("c", Qty((7, 1)), Start, Window);

        Assert.Equal(10m, order.Returns.SelectMany(r => r.Lines).Sum(l => l.Discount));   // 3,33 + 3,33 + 3,34
        Assert.Equal(290m, order.Returns.Sum(r => r.Amount));
    }

    [Fact]
    public void Cast_JenJednaRozpracovanaANeVicNezZbyva()
    {
        var order = Delivered();
        order.RequestReturn("a", Qty((7, 2)), Start, Window);

        Assert.Throws<DomainException>(() => order.RequestReturn("b", Qty((8, 1)), Start, Window));   // první ještě běží
        order.CompletePartialReturn(1, Start);
        Assert.Throws<DomainException>(() => order.RequestReturn("b", Qty((7, 2)), Start, Window));   // zbývá 1
        Assert.Throws<DomainException>(() => order.RequestReturn("b", Qty((99, 1)), Start, Window));  // není v objednávce
        Assert.Throws<DomainException>(() => order.RequestReturn("b", Qty((7, 0)), Start, Window));   // nic nevybráno
        Assert.Throws<DomainException>(() => order.RequestReturn("b", Qty((7, 1)), Start.AddDays(15), Window));   // po termínu
        Assert.Equal(2, order.RequestReturn("b", Qty((7, 1), (8, 1)), Start, Window));
    }

    [Fact]
    public void ZamitnutaCast_KusySeVraciDoNabidky()
    {
        var order = Delivered();
        order.RequestReturn("a", Qty((8, 1)), Start, Window);
        order.RejectPartialReturn(1, "Poškozené", Start);

        Assert.Equal(1, order.ReturnableQuantity(8));
        Assert.Equal((OrderReturnStatus.Rejected, "Poškozené"), (order.GetReturn(1).Status, order.GetReturn(1).RejectionReason));
        Assert.False(order.IsActive);
        Assert.Equal(2, order.RequestReturn("b", Qty((8, 1)), Start, Window));
    }

    [Fact]
    public void VsechnoJakoPrvni_JeVratkaCeleObjednavky()
    {
        var order = Delivered();

        Assert.Equal(0, order.RequestReturn("Celé", Qty((7, 3), (8, 1)), Start, Window));

        Assert.Equal(OrderStatus.ReturnRequested, order.Status);
        Assert.Empty(order.Returns);
        Assert.IsType<OrderReturnRequested>(Assert.Single(order.DomainEvents, e => e is not OrderStatusChanged));   // F3a: + obecná změna stavu
    }

    [Fact]
    public void PoVratcePoCastech_ZbytekUzJenPoCastechABezDopravy()
    {
        var order = Delivered();
        order.RequestReturn("a", Qty((7, 1)), Start, Window);
        order.CompletePartialReturn(1, Start);

        Assert.False(order.CanRequestReturn(Start, Window));   // celou objednávku (s dopravou) už ne
        Assert.Equal(2, order.RequestReturn("zbytek", Qty((7, 2), (8, 1)), Start, Window));
        order.CompletePartialReturn(2, Start);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal([7, 8], order.FullyReturnedProductIds());
        Assert.False(order.CanRequestPartialReturn(Start, Window));   // nic nezbývá
        Assert.Equal(order.ItemsTotal, order.Returns.Sum(r => r.Amount));   // doprava zůstává
    }

    [Fact]
    public void PoVratceCeleObjednavky_UzZadnaDalsi()
    {
        var order = Delivered();
        order.RequestReturn("Celé", Start, Window);
        order.RejectReturn("Poškozené", Start);

        Assert.False(order.CanRequestPartialReturn(Start, Window));
        Assert.Throws<DomainException>(() => order.RequestReturn("a", Qty((7, 1)), Start, Window));
    }

    [Fact]
    public void Saga_CisloVratky()
    {
        var saga = ReturnSaga.Start(42, Start, Window, requiresRefund: true, returnNumber: 2);

        Assert.Equal((2, true), (saga.ReturnNumber, saga.IsPartial));
        Assert.False(ReturnSaga.Start(42, Start, Window, requiresRefund: true).IsPartial);
        Assert.Throws<DomainException>(() => ReturnSaga.Start(42, Start, Window, requiresRefund: true, returnNumber: -1));
    }
}
