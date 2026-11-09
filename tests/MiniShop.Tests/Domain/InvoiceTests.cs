using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Invoicing;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 15: pravidla faktur – kdy jde fakturovat, číselná řada, zprávy mimo pořadí – bez databáze.</summary>
public class InvoiceTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CiselnaRada_RokAPoradiNaSestMist()
    {
        var series = InvoiceNumberSeries.Start(2026);

        Assert.Equal(["2026000001", "2026000002"], new[] { series.Next(), series.Next() });
        Assert.Equal((2, 2), (series.LastSequence, series.Version));
    }

    [Fact]
    public void Faktura_JenZeZaplaceneObjednavkySUdaji()
    {
        var paidOnly = BillableOrder.Create(5);
        paidOnly.MarkPaid(Now, "Kartou");
        var placedOnly = Placed(6);

        Assert.Throws<DomainException>(() => Invoice.Issue(paidOnly, "2026000001", Now));    // nevíme komu a co
        Assert.Throws<DomainException>(() => Invoice.Issue(placedOnly, "2026000001", Now));  // nezaplaceno
    }

    [Fact]
    public void ZpravyMimoPoradi_PlatbaPredVznikem_VysledekStejny()
    {
        var order = BillableOrder.Create(5);
        order.MarkPaid(Now, "Kartou");
        Assert.False(order.IsReadyToInvoice);

        ApplyPlaced(order);

        var invoice = Invoice.Issue(order, "2026000001", Now.AddMinutes(1));
        Assert.Equal((5, "Eva", 250m, "Kartou"), (invoice.OrderId, invoice.CustomerName, invoice.Total, invoice.PaymentNote));
        Assert.Equal(Now, invoice.PaidAtUtc);
        Assert.Single(invoice.Lines);
    }

    [Fact]
    public void Zaplaceno_PlatiPrvniZprava()
    {
        var order = Placed(5);

        order.MarkPaid(Now, "Kartou");
        order.MarkPaid(Now.AddDays(2), "Při převzetí");   // doručení karetní objednávky

        Assert.Equal((Now, "Kartou"), (order.PaidAtUtc, order.PaymentNote));
    }

    [Fact]
    public void ZamitnutaNeboZrusena_SeNefakturuje()
    {
        var order = Placed(5);
        order.MarkVoided(CreditNoteReason.Rejected, "Vráceno na kartu");
        order.MarkPaid(Now, "Kartou");   // pozdní platba – vrátí ji sága

        Assert.False(order.IsReadyToInvoice);
    }

    [Fact]
    public void Vratka_FakturovatDalJde_DuvodDobropisuPlatiPrvni()
    {
        var order = Placed(5);
        order.MarkPaid(Now, "Kartou");

        order.MarkReturned("Vráceno na kartu");
        order.MarkVoided(CreditNoteReason.Cancelled, "jiný text");   // pozdní zpráva důvod nepřepíše

        Assert.True(order.IsVoided);
        Assert.Equal((CreditNoteReason.Returned, "Vráceno na kartu"), (order.CreditReason, order.RefundNote));
    }

    [Fact]
    public void Dobropis_KopieFakturyVlastniRada()
    {
        var order = Placed(5);
        order.MarkPaid(Now, "Kartou");
        var invoice = Invoice.Issue(order, "2026000007", Now);
        var series = CreditNoteNumberSeries.Start(2026);

        var creditNote = CreditNote.Issue(invoice, series.Next(), CreditNoteReason.Returned, "Vrátíme převodem na účet", Now.AddDays(3));

        Assert.Equal(("D2026000001", "2026000007", 5, "Eva", 250m), (creditNote.Number, creditNote.InvoiceNumber,
            creditNote.OrderId, creditNote.CustomerName, creditNote.Total));
        Assert.Equal(invoice.Lines, creditNote.Lines);
        Assert.Throws<DomainException>(() => CreditNote.Issue(invoice, "D2026000002", CreditNoteReason.Cancelled, "x", Now.AddDays(-1)));
    }

    private static BillableOrder Placed(int orderId)
    {
        var order = BillableOrder.Create(orderId);
        ApplyPlaced(order);
        return order;
    }

    private static void ApplyPlaced(BillableOrder order) =>
        order.ApplyPlaced("zakaznik-eva", "Eva", "Dlouhá 5, Praha", "Courier", 50m, 250m, Now.AddHours(-1),
            [new InvoiceLine("Myš", 2, 100m, 0m, 200m)]);
}
