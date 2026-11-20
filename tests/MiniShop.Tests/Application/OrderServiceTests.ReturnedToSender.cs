using MiniShop.Application.Ordering;
using MiniShop.Contracts.Invoicing;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Invoicing;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Domain.Payments;
using MiniShop.Domain.Reporting;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 26D: dopravce zásilku nedoručí (2× nezastižen) a vrátí ji. Ordering jen oznámí OrderReturnedToSenderV1
/// a každý modul si udělá svoje (choreografie): sklad naskladní, Payments u karty vrátí peníze, Invoicing vystaví
/// dobropis (dobírka = nefakturovat), Notifications napíše, Reporting nepočítá do tržby.
/// </summary>
public sealed partial class OrderServiceTests
{
    /// <summary>Kroky nedoručitelné zásilky u simulátoru (převzato … 2× nezastižen … vráceno odesílateli).</summary>
    private const int UndeliverableSteps = 8;

    [Fact]
    public async Task VracenoDopravcem_Dobirka_ZboziZpetBezPenezABezFaktury()
    {
        UseCarrier(("Carrier:Chaos:UndeliverableRate", "1"));
        var order = await ShippedByCarrierAsync();
        Assert.Equal(9, await StockAsync());

        await AdvanceCarrierAsync(4);   // … doručuje se → nezastižen
        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Equal("DeliveryFailed", (await TrackingAsync(order.Id)).Status);

        await AdvanceCarrierAsync(UndeliverableSteps - 4);

        Assert.Equal("ReturnedToSender", await StatusAsync(order.Id));
        Assert.Equal(
            ["PickedUp", "InTransit", "OutForDelivery", "DeliveryFailed", "OutForDelivery", "DeliveryFailed", "ReturningToSender", "ReturnedToSender"],
            (await TrackingAsync(order.Id)).Steps.Select(s => s.Status));
        Assert.Single(_bus.Published.OfType<ShipmentReturnedV1>());
        var returned = Assert.Single(_bus.Published.OfType<OrderReturnedToSenderV1>());
        Assert.False(returned.RefundedByCard);
        Assert.Empty(_bus.Published.OfType<OrderCompletedV1>());

        Assert.Equal(10, await StockAsync());                                          // sklad naskladnil
        Assert.Equal(OrderSagaState.ReturnedToSender, (await SagaAsync(order.Id)).State);
        Assert.Empty(_bus.Published.OfType<PaymentRefundedV1>());                      // dobírka nebyla zaplacena
        Assert.Null(await InvoiceAsync(order.Id));                                     // ani se nefakturuje
        Assert.Null(await CreditNoteAsync(order.Id));
        Assert.Contains(_emails.Sent, e => e.Subject.Contains("nepodařilo doručit") && e.TextBody.Contains("dobírku neplatíte"));

        var report = await Report(s => s.GetAsync());
        Assert.Equal((1, 0m), (report.StatusCounts[nameof(SalesOrderStatus.ReturnedToSender)], report.Today.Revenue));
    }

    [Fact]
    public async Task VracenoDopravcem_Karta_PenizeZpetDobropisAZboziNaSklad()
    {
        UseCarrier(("Carrier:Chaos:UndeliverableRate", "1"));
        var order = await ShippedCardOrderByCarrierAsync();
        var invoice = await InvoiceAsync(order.Id) ?? throw new InvalidOperationException("Faktura po platbě nevznikla.");

        await AdvanceCarrierAsync(UndeliverableSteps);

        Assert.Equal("ReturnedToSender", await StatusAsync(order.Id));
        Assert.True(Assert.Single(_bus.Published.OfType<OrderReturnedToSenderV1>()).RefundedByCard);
        Assert.Equal(10, await StockAsync());
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusAsync(order.Id));
        Assert.Equal(order.TotalPrice, Assert.Single(_bus.Published.OfType<PaymentRefundedV1>()).Amount);

        var creditNote = await CreditNoteAsync(order.Id) ?? throw new InvalidOperationException("Dobropis nevznikl.");
        Assert.Equal((CreditNoteReason.ReturnedToSender, invoice.Total), (creditNote.Reason, creditNote.Total));
        Assert.Single(_bus.Published.OfType<CreditNoteIssuedV1>());

        Assert.Contains(_emails.Sent, e => e.Subject.Contains("nepodařilo doručit") && e.TextBody.Contains("na kartu"));
        Assert.Contains(_emails.Sent, e => e.Subject.Contains("vracíme"));   // e-mail z Payments (PaymentRefundedV1) jako u zrušení
        Assert.Equal(0m, (await Report(s => s.GetAsync())).Today.Revenue);
    }

    [Fact]
    public async Task VracenoDopravcem_ZpravyDvakrat_KompenzaceJenJednou()
    {
        UseCarrier(("Carrier:Chaos:UndeliverableRate", "1"), ("Carrier:Chaos:DuplicateRate", "1"), ("Carrier:Chaos:ReorderRate", "1"));
        var order = await ShippedCardOrderByCarrierAsync();

        await AdvanceCarrierAsync(UndeliverableSteps + 2);   // duplicity a zpožděné webhooky doběhnou

        Assert.Equal("ReturnedToSender", await StatusAsync(order.Id));
        Assert.Equal("ReturnedToSender", (await TrackingAsync(order.Id)).Status);   // pozdní „vrací se“ stav nevrátil zpět
        Assert.Single(_bus.Published.OfType<ShipmentReturnedV1>());

        // Stejná událost od Orderingu podruhé (jako by ji broker doručil znovu s novým ID) – nikdo nic nevrátí dvakrát.
        var returned = _bus.Published.OfType<OrderReturnedToSenderV1>().Single();
        await _bus.PublishAsync(returned with { EventId = Guid.NewGuid() });
        await ProcessOutboxAsync();

        Assert.Equal(10, await StockAsync());
        Assert.Single(_bus.Published.OfType<PaymentRefundedV1>());
        Assert.Single(_bus.Published.OfType<CreditNoteIssuedV1>());
    }

    [Fact]
    public async Task VracenoDopravcem_PozdeAzPoRucnimDoruceni_NicSeNemeni()
    {
        UseCarrier(("Carrier:Chaos:UndeliverableRate", "1"));
        var order = await ShippedByCarrierAsync();
        await AdvanceCarrierAsync(4);

        // Zásilka „zmizela“ – skladník ji označil jako doručenou ručně (UnknownAtCarrier v Expedici). Pak dorazí návrat.
        await Orders(s => s.CompleteAsync(order.Id, Petra.CustomerId));
        await ProcessOutboxAsync();
        await AdvanceCarrierAsync(UndeliverableSteps - 4);

        Assert.Equal("Completed", await StatusAsync(order.Id));
        Assert.Single(_bus.Published.OfType<ShipmentReturnedV1>());
        Assert.Empty(_bus.Published.OfType<OrderReturnedToSenderV1>());   // Ordering událost nevyrobil – nikdo nic nevrací
    }

    [Fact]
    public async Task VracenoDopravcem_PrestaveniPrehledu_StavZustane()
    {
        UseCarrier(("Carrier:Chaos:UndeliverableRate", "1"));
        var order = await ShippedByCarrierAsync();
        await AdvanceCarrierAsync(UndeliverableSteps);
        Assert.Equal("ReturnedToSender", await StatusAsync(order.Id));

        await Report(async s => { await s.RebuildAsync(); return true; });
        await ProcessOutboxAsync();   // RebuildSalesReportV1 → OrderSnapshotV1 se stavem ReturnedToSender

        var report = await Report(s => s.GetAsync());
        Assert.Equal((1, 0m), (report.StatusCounts[nameof(SalesOrderStatus.ReturnedToSender)], report.Today.Revenue));
    }

    /// <summary>Kurýr + karta: zaplatit na simulované bráně, zabalit, předat dopravci.</summary>
    private async Task<OrderDto> ShippedCardOrderByCarrierAsync()
    {
        var product = await AddProductAsync(price: 300m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        var checkout = await Checkout(s => s.GetAsync(Jan.CustomerId));
        var request = Courier(checkout.CheckoutId);
        request.PaymentMethod = PaymentMethod.Card;
        var order = await Checkout(s => s.PlaceOrderAsync(request, Jan));
        await ProcessOutboxAsync();
        await SimulateGatewayAsync(order.Id, paid: true);
        await ProcessOutboxAsync();
        Assert.Equal("Confirmed", await StatusAsync(order.Id));

        await Orders(s => s.StartPackingAsync(order.Id));
        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        await ProcessOutboxAsync();
        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Equal(9, await StockAsync());
        return order;
    }
}
