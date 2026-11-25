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
        var order = await PackedSplitOrderAsync();   // myš z Prahy, kabel z Brna → dva balíky

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
        var order = await PackedSplitOrderAsync();

        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));   // generátor provozu, starší tlačítko
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId)));
        await ProcessOutboxAsync();

        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Equal(2, _bus.Published.OfType<CreateShipmentV2>().Count());
    }

    /// <summary>Zabalená objednávka kurýrem na dobírku: myš jen v Praze, kabel jen v Brně – sklad ji rozdělí na dva balíky.</summary>
    private async Task<OrderDto> PackedSplitOrderAsync()
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
        return order;
    }
}
