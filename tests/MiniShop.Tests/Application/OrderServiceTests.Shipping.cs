using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Ordering;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Shipping;
using MiniShop.Tests.CarrierSimulator;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 26B: předání zásilky dopravci – Ordering → CreateShipmentV1 → Shipping → API dopravce (simulátor v paměti
/// přes HTTP) → ShipmentCreatedV1 / ShipmentFailedV1 → Ordering. Testy celé cesty včetně „zlobení“ dopravce.
/// </summary>
public sealed partial class OrderServiceTests
{
    /// <summary>Dopravce (simulátor). Výchozí spolehlivý; <see cref="UseCarrier"/> ho vymění za zlobivého.</summary>
    private CarrierSimulatorFactory Carrier => _carrier ??= new CarrierSimulatorFactory();

    private void UseCarrier(params (string Key, string Value)[] settings)
    {
        _carrier?.Dispose();
        _carrier = new CarrierSimulatorFactory(settings.ToDictionary(s => s.Key, s => (string?)s.Value));
    }

    [Fact]
    public async Task PredatDopravci_CisloZasilkyPrideliDopravce_ObjednavkaOdeslana()
    {
        var order = await PackedCourierOrderAsync();

        var handedOver = await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        Assert.True(handedOver.AwaitingShipment);
        await ProcessOutboxAsync();

        var shipped = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal("Shipped", shipped.Status);
        Assert.Equal(CarrierApiClient.CarrierName, shipped.Carrier);
        Assert.Matches(@"^CS\d{10}$", shipped.TrackingNumber);
        Assert.False(shipped.AwaitingShipment);

        // U dopravce právě jedna zásilka – reference = číslo objednávky.
        var atCarrier = Assert.Single(Carrier.Services.GetRequiredService<MiniShop.CarrierSimulator.ShipmentStore>()
            .Read(s => s.Shipments.Select(x => (x.TrackingNumber, x.Reference, x.CashOnDelivery)).ToList()));
        Assert.Equal((shipped.TrackingNumber, order.Id.ToString(), (decimal?)order.TotalPrice), atCarrier);

        // Dál beze změny: OrderShippedV1 s číslem od dopravce (sklad vydá rezervaci, zákazník dostane e-mail).
        var published = Assert.Single(_bus.Published.OfType<OrderShippedV1>());
        Assert.Equal(shipped.TrackingNumber, published.TrackingNumber);
        Assert.Contains(_emails.Sent, e => e.TextBody.Contains(shipped.TrackingNumber!));
    }

    [Fact]
    public async Task ZtracenaOdpovedDopravce_Opakovani_StejnaZasilkaZadnaDruha()
    {
        UseCarrier(("Carrier:Chaos:LostResponseRate", "1"));   // zásilka vznikne, ale odpověď „se ztratí“ (503)
        var order = await PackedCourierOrderAsync();

        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        await ProcessOutboxAsync();
        Assert.True((await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders))).AwaitingShipment);   // zatím neví

        _time.Advance(OutboxProcessor.RetryDelay(1));
        await ProcessOutboxAsync();   // opakování se stejným Idempotency-Key → dopravce vrátí tutéž zásilku

        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Equal(1, Carrier.Services.GetRequiredService<MiniShop.CarrierSimulator.ShipmentStore>().Read(s => s.Shipments.Count));
    }

    [Fact]
    public async Task DopravceOdmitne_ZpetKOdeslaniSHlaskou_ZnovuJdePredat()
    {
        UseCarrier(("Carrier:Chaos:RejectRate", "1"));   // „na tuto adresu nedoručujeme“
        var order = await PackedCourierOrderAsync();

        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        await ProcessOutboxAsync();

        var failed = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal(("Packing", false, "Dopravce na tuto adresu nedoručuje."), (failed.Status, failed.AwaitingShipment, failed.ShipmentError));
        Assert.Single(_bus.Published.OfType<ShipmentFailedV1>());

        // Skladník zkusí znovu (u dopravce to tentokrát projde) – nový příkaz, nové ID, hláška zmizí.
        UseCarrier();
        var again = await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        Assert.Null(again.ShipmentError);
        await ProcessOutboxAsync();
        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Equal(2, _bus.Published.OfType<CreateShipmentV1>().Select(c => c.CommandId).Distinct().Count());
    }

    [Fact]
    public async Task DopravceNeodpovida_ObjednavkaCekaANicNevznikne()
    {
        UseCarrier(("Carrier:Chaos:FailureRate", "1"));   // 503 bez vytvoření
        var order = await PackedCourierOrderAsync();

        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        await ProcessOutboxAsync();

        var waiting = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal(("Packing", true), (waiting.Status, waiting.AwaitingShipment));
        Assert.Empty(_bus.Published.OfType<ShipmentCreatedV1>());
        Assert.Empty(_bus.Published.OfType<ShipmentFailedV1>());   // dočasná chyba = opakovat, ne odmítnout
    }

    [Fact]
    public async Task PoPredaniDopravci_ZrusitAniPredatZnovuNejde()
    {
        UseCarrier(("Carrier:Chaos:FailureRate", "1"));   // ať objednávka na dopravce čeká
        var order = await PackedCourierOrderAsync();
        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));

        var cancel = await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders)));
        Assert.Contains("předává dopravci", cancel.Message);
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId)));   // dvojklik
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.ShipAsync(order.Id, "PPL", "1", Petra.CustomerId)));   // ručně ne
    }

    [Fact]
    public async Task OsobniOdber_DopravciPredatNejde_StretZajmuPlati()
    {
        var product = await AddProductAsync(price: 200m, stock: 5);
        var pickup = await PlaceCardOrderAsync(product.Id, quantity: 1, PaymentMethod.CashOnDelivery);   // osobní odběr
        await ProcessOutboxAsync();
        await Orders(s => s.StartPackingAsync(pickup.Id));
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.RequestShipmentAsync(pickup.Id, Petra.CustomerId)));

        var own = await PackedCourierOrderAsync();
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.RequestShipmentAsync(own.Id, Jan.CustomerId)));   // vlastní objednávku ne
    }

    /// <summary>Zabalená objednávka kurýrem na dobírku (Jan, Dlouhá 5, Praha).</summary>
    private async Task<OrderDto> PackedCourierOrderAsync()
    {
        var product = await AddProductAsync(price: 300m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        var checkout = await Checkout(s => s.GetAsync(Jan.CustomerId));
        var order = await Checkout(s => s.PlaceOrderAsync(Courier(checkout.CheckoutId), Jan));
        await ProcessOutboxAsync();
        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        await Orders(s => s.StartPackingAsync(order.Id));
        return order;
    }
}
