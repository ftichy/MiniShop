using MiniShop.Domain.Invoicing;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 28D: vrácený balík dobírky dorazí do fakturace dřív, než je objednávka zaplacená – kusy se odečtou z kopie,
/// faktura po zaplacení zbytku bude jen za převzaté zboží (bez dobropisu). Vrátí se všechno = nic k fakturaci.
/// </summary>
public sealed class BillableOrderReturnTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

    private static BillableOrder Order()
    {
        var order = BillableOrder.Create(7);
        order.ApplyPlaced("zakaznik-1", "Jan Novák", "Dlouhá 5, Praha", "Courier", 99m, 599m, Now,
            [new InvoiceLine("Myš", 1, 300m, 0m, 300m), new InvoiceLine("Kabel", 2, 100m, 0m, 200m)]);
        return order;
    }

    [Fact]
    public void VracenyBalikPredFakturou_OdectezKopie_FakturaJenZaPrevzate()
    {
        var order = Order();

        Assert.True(order.ApplyReturnBeforeInvoice(1, [new InvoiceLine("Kabel", 2, 100m, 0m, 200m)], 200m));
        Assert.False(order.ApplyReturnBeforeInvoice(1, [new InvoiceLine("Kabel", 2, 100m, 0m, 200m)], 200m));   // opakovaně nic

        order.MarkPaid(Now, "Uhrazeno při převzetí");
        Assert.True(order.IsReadyToInvoice);
        var invoice = Invoice.Issue(order, "F2026000001", Now);
        Assert.Equal(399m, invoice.Total);   // myš + doprava
        Assert.Equal(["Myš"], invoice.Lines.Select(l => l.ProductName));
    }

    [Fact]
    public void VratiloSeVsechno_NicKFakturaci()
    {
        var order = Order();

        order.ApplyReturnBeforeInvoice(1, [new InvoiceLine("Myš", 1, 300m, 0m, 300m)], 300m);
        order.ApplyReturnBeforeInvoice(2, [new InvoiceLine("Kabel", 2, 100m, 0m, 200m)], 200m);
        order.MarkPaid(Now, "Uhrazeno při převzetí");

        Assert.False(order.IsReadyToInvoice);
    }
}
