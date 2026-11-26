using MiniShop.Application.Ordering;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 28C (směr C): rozdělená objednávka – každý balík předá jeho sklad zvlášť, má vlastní zásilku u dopravce.
/// Odeslaná je objednávka s posledním balíkem, doručená až s posledním doručeným (scatter-gather v agregátu Order).
/// </summary>
public sealed partial class OrderServiceTests
{
    [Fact]
    public async Task Baliky_KazdyVlastniZasilka_OdeslanoSPoslednim_DorucenoAzPoVsech()
    {
        var (order, _, _) = await PackedSplitOrderAsync();   // myš z Prahy, kabel z Brna → dva balíky

        await Orders(s => s.RequestParcelShipmentAsync(order.Id, 1, Petra.CustomerId));
        await ProcessOutboxAsync();

        var half = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal("Packing", half.Status);   // druhý balík ještě u nás
        Assert.Equal(["Shipped", "Waiting"], half.Parcels!.Select(p => p.Status));
        var parcelShipped = Assert.Single(_bus.Published.OfType<ParcelShippedV1>());
        Assert.Equal((1, 2), (parcelShipped.ParcelNumber, parcelShipped.ParcelCount));
        Assert.Contains(_emails.Sent, e => e.Subject.Contains($"Balík 1 z 2 objednávky č. {order.Id}"));
        Assert.Empty(_bus.Published.OfType<OrderShippedV1>());
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders)));   // balík už jede

        await Orders(s => s.RequestParcelShipmentAsync(order.Id, 2, Petra.CustomerId));
        await ProcessOutboxAsync();

        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Single(_bus.Published.OfType<OrderShippedV1>());   // objednávka jako celek odešla jednou
        var commands = _bus.Published.OfType<CreateShipmentV2>().ToList();
        Assert.Equal([1, 2], commands.Select(c => c.ParcelNumber).Order());
        Assert.Equal(2, commands.Select(c => c.CommandId).Distinct().Count());   // vlastní Idempotency-Key
        Assert.Equal(half.TotalPrice, commands.Sum(c => c.CashOnDelivery));     // dobírka po balících = celá objednávka

        await AdvanceCarrierAsync(4);   // dopravce oba balíky doručí

        var delivered = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal("Completed", delivered.Status);
        Assert.All(delivered.Parcels!, p => Assert.Equal("Delivered", p.Status));
        Assert.Equal(2, delivered.Parcels!.Select(p => p.TrackingNumber).Distinct().Count());
    }

    [Fact]
    public async Task Baliky_PredatBezCisla_PredaVsechny_DvojklikChyba()
    {
        var (order, _, _) = await PackedSplitOrderAsync();

        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));   // generátor provozu, starší tlačítko
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId)));
        await ProcessOutboxAsync();

        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Equal(2, _bus.Published.OfType<CreateShipmentV2>().Count());
    }

    /// <summary>
    /// E-shop krok 28D: dopravce vrátí oba balíky rozdělené objednávky. Každý vrácený balík = automatická vratka po částech
    /// (sklad naskladní do skladu původu, ReturnCompletedV2); objednávka skončí „vráceno dopravcem“ BEZ OrderReturnedToSenderV1 –
    /// ta by vše kompenzovala podruhé.
    /// </summary>
    [Fact]
    public async Task Baliky_VracenyDopravcem_KazdyVlastniVratkaPoCastech_BezDvojiKompenzace()
    {
        UseCarrier(("Carrier:Chaos:UndeliverableRate", "1"));
        var (order, mouseId, cableId) = await PackedSplitOrderAsync();
        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        await ProcessOutboxAsync();
        Assert.Equal("Shipped", await StatusAsync(order.Id));

        await AdvanceCarrierAsync(UndeliverableSteps);
        await ProcessOutboxAsync();   // dokončení vratek (sklad → sága → ReturnCompletedV2)

        var returned = await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal("ReturnedToSender", returned.Order.Status);
        Assert.All(returned.Order.Parcels!, p => Assert.Equal("ReturnedToSender", p.Status));
        Assert.Equal(2, returned.Returns!.Count);
        Assert.All(returned.Returns!, r => Assert.Equal("Completed", r.Status));   // dobírka: bez refundu rovnou hotovo
        Assert.Equal(2, _bus.Published.OfType<ReturnCompletedV2>().Count());
        Assert.Empty(_bus.Published.OfType<OrderReturnedToSenderV1>());
        Assert.DoesNotContain(_bus.Published.OfType<ReturnRequestedV2>(), _ => true);   // zákazník nic neposílá – žádný e-mail „pošlete zboží“

        // Zboží zpět do skladů původu: myš do Prahy, kabely do Brna.
        var stocks = await Products(s => s.GetAllAsync());
        Assert.Equal([(Warehouse.DefaultId, 5)], StocksOf(stocks, mouseId));
        Assert.Equal([(Warehouse.BrnoId, 5)], StocksOf(stocks, cableId));
    }

    private static IEnumerable<(int, int)> StocksOf(IReadOnlyList<MiniShop.Application.Inventory.ProductDto> products, int productId) =>
        products.Single(p => p.Id == productId).Stocks!.Select(s => (s.WarehouseId, s.Quantity));

    /// <summary>Zabalená objednávka kurýrem na dobírku: myš jen v Praze, kabel jen v Brně – sklad ji rozdělí na dva balíky.</summary>
    private async Task<(OrderDto Order, int MouseId, int CableId)> PackedSplitOrderAsync()
    {
        var mouse = await AddProductAsync(price: 300m, stock: 5);                     // Praha
        var cable = await AddProductAsync(price: 100m, stock: 0);
        await Products(s => s.AddStockAsync(cable.Id, 5, Warehouse.BrnoId));          // Brno
        await ProcessOutboxAsync();

        await Cart(s => s.AddItemAsync(Jan.CustomerId, mouse.Id, 1));
        await Cart(s => s.AddItemAsync(Jan.CustomerId, cable.Id, 2));
        var checkout = await Checkout(s => s.GetAsync(Jan.CustomerId));
        var order = await Checkout(s => s.PlaceOrderAsync(Courier(checkout.CheckoutId), Jan));
        await ProcessOutboxAsync();
        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        await Orders(s => s.StartPackingAsync(order.Id));
        Assert.Equal(2, (await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders))).Parcels!.Count);
        return (order, mouse.Id, cable.Id);
    }
}
