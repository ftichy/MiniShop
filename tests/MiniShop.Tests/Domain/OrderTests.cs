using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Domain;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Order CreateOrder() => new("zakaznik-1", "Jan Novák", "jan@example.com", Now);

    private static Order CreateConfirmedOrder(ShippingMethod shipping = ShippingMethod.Courier)
    {
        var order = CreateOrder();
        order.SetShipping(shipping, 99m, shipping == ShippingMethod.Courier ? new DeliveryAddress("Dlouhá 5", "Praha", "11000") : null);
        order.Confirm(Now);
        return order;
    }

    /// <summary>E-shop krok 6: objednávka na cestě (kurýr PPL).</summary>
    private static Order CreateShippedOrder()
    {
        var order = CreateConfirmedOrder();
        order.StartPacking(Now);
        order.Ship("PPL", "123456", Now);
        return order;
    }

    [Fact]
    public void TotalPrice_SectePolozkyVcetneSlevy()
    {
        var order = CreateOrder();
        order.AddItem(new CatalogProduct(1, "A", 100m, Now), quantity: 2, discount: 0m);
        order.AddItem(new CatalogProduct(2, "B", 50m, Now), quantity: 4, discount: 20m);

        Assert.Equal(200m + 180m, order.TotalPrice);
    }

    [Fact]
    public void NovaObjednavka_CekaNaSklad()
    {
        Assert.Equal(OrderStatus.Pending, CreateOrder().Status);
    }

    [Fact]
    public void Reject_UloziDuvod()
    {
        var order = CreateOrder();

        order.Reject("Nedostatek zboží", Now);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Nedostatek zboží", order.RejectionReason);
    }

    [Fact]
    public void Reject_BezDuvodu_VyhodiVyjimku()
    {
        Assert.Throws<DomainException>(() => CreateOrder().Reject(" ", Now));
    }

    [Fact]
    public void Confirm_ZamitnutouObjednavkuNelzePotvrdit()
    {
        var order = CreateOrder();
        order.Reject("Nedostatek zboží", Now);

        Assert.Throws<DomainException>(() => order.Confirm(Now));
    }

    [Fact]
    public void CekajiciObjednavku_JdeZrusit_AleNeDorucit()
    {
        var order = CreateOrder();

        Assert.Throws<DomainException>(() => order.MarkDelivered(Now));
        order.Cancel(Now);   // e-shop krok 9: zrušit jde i před odpovědí skladu

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void ZamitnutouObjednavku_NelzeZrusit()
    {
        var order = CreateOrder();
        order.Reject("Nedostatek zboží", Now);

        Assert.Throws<DomainException>(() => order.Cancel(Now));
    }

    [Fact]
    public void Cancel_ZrusenouObjednavkuNelzeZrusitZnovu()
    {
        var order = CreateConfirmedOrder();
        order.Cancel(Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Throws<DomainException>(() => order.Cancel(Now));
    }

    [Fact]
    public void MarkDelivered_ZrusenouObjednavkuNelzeDorucit()
    {
        var order = CreateConfirmedOrder();
        order.Cancel(Now);

        Assert.Throws<DomainException>(() => order.MarkDelivered(Now));
    }

    [Fact]
    public void AddItem_DoPotvrzeneObjednavkyNelze()
    {
        var order = CreateConfirmedOrder();

        Assert.Throws<DomainException>(() => order.AddItem(new CatalogProduct(1, "A", 100m, Now), 1, 0m));
    }

    // --- E-shop krok 5: historie stavů ---

    [Fact]
    public void Historie_KazdyPrechodPridaRadekSCasem()
    {
        var order = CreateOrder();
        order.Confirm(Now.AddSeconds(4));
        order.StartPacking(Now.AddHours(1));
        order.Ship(null, null, Now.AddHours(2));   // objednávka bez dopravy (API) – dopravce nepovinný
        order.MarkDelivered(Now.AddDays(1));

        Assert.Equal(
            [(OrderStatus.Pending, Now), (OrderStatus.Confirmed, Now.AddSeconds(4)), (OrderStatus.Packing, Now.AddHours(1)),
             (OrderStatus.Shipped, Now.AddHours(2)), (OrderStatus.Completed, Now.AddDays(1))],
            order.StatusHistory.Select(c => (c.Status, c.ChangedAtUtc!.Value)));
    }

    [Fact]
    public void Historie_ZamitnutiNeseDuvod()
    {
        var order = CreateOrder();

        order.Reject(" Nedostatek zboží ", Now);

        var last = order.StatusHistory.Last();
        Assert.Equal((OrderStatus.Rejected, "Nedostatek zboží"), (last.Status, last.Note));
    }

    [Fact]
    public void Historie_NepovolenyPrechodNicNepridaNiNezmeni()
    {
        var order = CreateOrder();

        Assert.Throws<DomainException>(() => order.MarkDelivered(Now));

        Assert.Equal([OrderStatus.Pending], order.StatusHistory.Select(c => c.Status));
    }

    [Fact]
    public void Historie_AnonymizacePoznamkyNemaze()
    {
        var order = CreateOrder();
        order.Reject("Nedostatek zboží", Now);

        order.Anonymize();

        Assert.Equal("Nedostatek zboží", order.StatusHistory.Last().Note);   // důvod není osobní údaj
    }

    // --- E-shop krok 6: expedice ---

    [Fact]
    public void Expedice_KuryrBezCislaZasilky_NejdeOdeslat()
    {
        var order = CreateConfirmedOrder();
        order.StartPacking(Now);

        Assert.Throws<DomainException>(() => order.Ship("PPL", " ", Now));
        Assert.Equal(OrderStatus.Packing, order.Status);
    }

    [Fact]
    public void Expedice_OdeslaniUloziDopravceACisloDoObjednavkyIHistorie()
    {
        var order = CreateShippedOrder();

        Assert.Equal((OrderStatus.Shipped, "PPL", "123456"), (order.Status, order.Carrier, order.TrackingNumber));
        Assert.Equal("PPL, zásilka 123456", order.StatusHistory.Last().Note);
    }

    [Fact]
    public void Expedice_OsobniOdber_BezDopravceACisla()
    {
        var order = CreateConfirmedOrder(ShippingMethod.Pickup);
        order.StartPacking(Now);

        order.Ship("PPL", "123", Now);   // i když je admin vyplní, osobní odběr je nemá

        Assert.Equal((OrderStatus.Shipped, (string?)null, (string?)null), (order.Status, order.Carrier, order.TrackingNumber));
    }

    [Fact]
    public void Expedice_ZrusitJdeIBehemBaleni_PoOdeslaniUzNe()
    {
        var packing = CreateConfirmedOrder();
        packing.StartPacking(Now);
        packing.Cancel(Now);
        Assert.Equal(OrderStatus.Cancelled, packing.Status);

        var shipped = CreateShippedOrder();
        var error = Assert.Throws<DomainException>(() => shipped.Cancel(Now));
        Assert.Contains("Odeslanou objednávku", error.Message);
    }

    [Fact]
    public void Expedice_PreskocitStavNejde()
    {
        var order = CreateConfirmedOrder();

        Assert.Throws<DomainException>(() => order.Ship("PPL", "1", Now));      // bez balení
        Assert.Throws<DomainException>(() => order.MarkDelivered(Now));         // bez odeslání (dřív „Vyřídit“)
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Expedice_KazdaZmenaStavuZvysiVerzi()
    {
        var order = CreateConfirmedOrder();
        var before = order.Version;

        order.StartPacking(Now);

        Assert.Equal(before + 1, order.Version);
    }

    // --- Účty krok 9: anonymizace po smazání účtu ---

    [Fact]
    public void Anonymizace_VyrizenaObjednavka_ZtratiJmenoAEmail()
    {
        var order = CreateShippedOrder();
        Assert.Throws<DomainException>(() => order.Anonymize());   // e-shop krok 6: na cestě = rozpracovaná
        order.MarkDelivered(Now);

        order.Anonymize();

        Assert.Equal(Order.AnonymizedCustomerName, order.CustomerName);
        Assert.Equal(string.Empty, order.CustomerEmail);
        Assert.Equal("zakaznik-1", order.CustomerId);
    }

    [Fact]
    public void Anonymizace_RozpracovanaObjednavka_Nejde()
    {
        Assert.Throws<DomainException>(() => CreateOrder().Anonymize());            // Pending
        Assert.Throws<DomainException>(() => CreateConfirmedOrder().Anonymize());   // Confirmed
    }
}
