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
    /// <summary>
    /// Dopravce (simulátor). Výchozí spolehlivý; <see cref="UseCarrier"/> ho vymění za zlobivého. Hodiny simulátoru jdou
    /// s hodinami testu (<c>_time</c>) – podpis webhooku nese čas a e-shop starší než 5 minut odmítne.
    /// </summary>
    private CarrierSimulatorFactory Carrier
    {
        get
        {
            if (_carrier is null)
            {
                _carrier = new CarrierSimulatorFactory();
                _carrier.Time.Set(_time.GetUtcNow());
            }

            return _carrier;
        }
    }

    private void UseCarrier(params (string Key, string Value)[] settings)
    {
        _carrier?.Dispose();
        _carrier = new CarrierSimulatorFactory(settings.ToDictionary(s => s.Key, s => (string?)s.Value));
        _carrier.Time.Set(_time.GetUtcNow());
    }

    private int _forwardedWebhooks;

    /// <summary>
    /// E-shop krok 26C: posune čas o <paramref name="steps"/> kroků dopravce (oběma hodinám), simulátor pošle webhooky
    /// a ty se doručí webhook procesoru e-shopu (jako HTTP endpoint, se stejným tělem a podpisem); pak outboxy.
    /// </summary>
    private async Task AdvanceCarrierAsync(int steps = 1)
    {
        for (var i = 0; i < steps; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(CarrierSimulatorFactory.StepSeconds));
            Carrier.Time.Set(_time.GetUtcNow());
            await Carrier.ProcessAsync();
            await ForwardWebhooksAsync();
        }

        await ProcessOutboxAsync();
    }

    private async Task<List<CarrierWebhookResult>> ForwardWebhooksAsync()
    {
        var results = new List<CarrierWebhookResult>();
        foreach (var webhook in Carrier.Webhooks.Received.Skip(_forwardedWebhooks).ToList())
        {
            _forwardedWebhooks++;
            await using var scope = _services.CreateAsyncScope();
            results.Add(await scope.ServiceProvider.GetRequiredService<CarrierWebhookProcessor>().ProcessAsync(webhook.Body, webhook.Signature));
        }

        return results;
    }

    /// <summary>Předaná objednávka kurýrem: zabalit, předat dopravci, číslo zásilky.</summary>
    private async Task<OrderDto> ShippedByCarrierAsync()
    {
        var order = await PackedCourierOrderAsync();
        await Orders(s => s.RequestShipmentAsync(order.Id, Petra.CustomerId));
        await ProcessOutboxAsync();
        Assert.Equal("Shipped", await StatusAsync(order.Id));
        return order;
    }

    private async Task<MiniShop.Application.Shipping.ShipmentTrackingDto> TrackingAsync(int orderId)
    {
        await using var scope = _services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<MiniShop.Application.Shipping.ShipmentQueryService>().GetByOrderIdAsync(orderId))!;
    }

    [Fact]
    public async Task DopravceDoruci_WebhookyPosouvajiStav_ObjednavkaDorucena()
    {
        var order = await ShippedByCarrierAsync();

        await AdvanceCarrierAsync(2);
        Assert.Equal("Shipped", await StatusAsync(order.Id));
        Assert.Equal("InTransit", (await TrackingAsync(order.Id)).Status);

        await AdvanceCarrierAsync(2);

        Assert.Equal("Completed", await StatusAsync(order.Id));
        var tracking = await TrackingAsync(order.Id);
        Assert.Equal(["PickedUp", "InTransit", "OutForDelivery", "Delivered"], tracking.Steps.Select(s => s.Status));
        Assert.Single(_bus.Published.OfType<ShipmentDeliveredV1>());
        Assert.Single(_bus.Published.OfType<OrderCompletedV1>(), c => c.OrderId == order.Id);   // e-mail „doručeno“, faktura u dobírky …
    }

    [Fact]
    public async Task WebhookyDvakratAMimoPoradi_StavJenDopredu_DorucenoJednou()
    {
        UseCarrier(("Carrier:Chaos:DuplicateRate", "1"), ("Carrier:Chaos:ReorderRate", "1"));
        var order = await ShippedByCarrierAsync();

        await AdvanceCarrierAsync(6);

        Assert.Equal("Completed", await StatusAsync(order.Id));
        var tracking = await TrackingAsync(order.Id);
        Assert.Equal("Delivered", tracking.Status);   // „doručuje se“ přišlo po „doručeno“ – stav zůstal
        Assert.Equal(4, tracking.Steps.Count);         // duplicity se do historie nezapsaly
        Assert.Single(_bus.Published.OfType<ShipmentDeliveredV1>());
    }

    [Fact]
    public async Task WebhookySeZtrati_DohledaniDozeneDoruceni()
    {
        UseCarrier(("Carrier:Chaos:DropRate", "1"));   // dopravce neposílá nic
        var order = await ShippedByCarrierAsync();
        await AdvanceCarrierAsync(4);
        Assert.Equal("Shipped", await StatusAsync(order.Id));

        _time.Advance(TimeSpan.FromMinutes(3));   // ticho > 2 minuty
        Carrier.Time.Set(_time.GetUtcNow());
        await using (var scope = _services.CreateAsyncScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<MiniShop.Application.Shipping.ShipmentTrackingService>()
                .ReconcileAsync(TimeSpan.FromMinutes(2), batchSize: 20));
        }

        await ProcessOutboxAsync();
        Assert.Equal("Completed", await StatusAsync(order.Id));
    }

    [Fact]
    public async Task Webhook_PodpisCasObsahANeznamaZasilka()
    {
        var order = await ShippedByCarrierAsync();
        await AdvanceCarrierAsync(1);
        var genuine = Carrier.Webhooks.Received[0];

        async Task<CarrierWebhookResult> SendAsync(string body, string? signature)
        {
            await using var scope = _services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CarrierWebhookProcessor>().ProcessAsync(body, signature);
        }

        string Sign(string body, DateTimeOffset at) =>
            MiniShop.CarrierSimulator.WebhookDispatcher.Sign(CarrierSimulatorFactory.WebhookSecret, body, at);

        // Podvržený obsah s původním podpisem, bez podpisu, cizí tajemství → 401.
        var forged = genuine.Body.Replace("accepted", "delivered");
        Assert.Equal(CarrierWebhookResult.InvalidSignature, await SendAsync(forged, genuine.Signature));
        Assert.Equal(CarrierWebhookResult.InvalidSignature, await SendAsync(genuine.Body, null));
        Assert.Equal(CarrierWebhookResult.InvalidSignature, await SendAsync(genuine.Body,
            MiniShop.CarrierSimulator.WebhookDispatcher.Sign(new string('x', 64), genuine.Body, _time.GetUtcNow())));

        // Přehraná stará zpráva (podpis platný, ale 10 minut starý) → 401.
        Assert.Equal(CarrierWebhookResult.InvalidSignature, await SendAsync(genuine.Body, Sign(genuine.Body, _time.GetUtcNow().AddMinutes(-10))));

        // Platný podpis, ale nečitelné tělo → 400; cizí zásilka → 200 (dopravce nemá opakovat), nic se nezmění.
        Assert.Equal(CarrierWebhookResult.Invalid, await SendAsync("{nesmysl", Sign("{nesmysl", _time.GetUtcNow())));
        var unknown = genuine.Body.Replace(Carrier.Services.GetRequiredService<MiniShop.CarrierSimulator.ShipmentStore>()
            .Read(s => s.Shipments[0].TrackingNumber), "CS0000000000");
        Assert.Equal(CarrierWebhookResult.Accepted, await SendAsync(unknown, Sign(unknown, _time.GetUtcNow())));
        Assert.Equal("PickedUp", (await TrackingAsync(order.Id)).Status);
    }

    [Fact]
    public async Task DorucenoRucneDriv_PozdniWebhookNicNezmeni()
    {
        var order = await ShippedByCarrierAsync();
        await Orders(s => s.CompleteAsync(order.Id, Petra.CustomerId));   // skladník klikl „Doručeno“ (zásilka ztracená u dopravce)
        await ProcessOutboxAsync();

        await AdvanceCarrierAsync(4);

        Assert.Equal("Completed", await StatusAsync(order.Id));
        Assert.Single(_bus.Published.OfType<OrderCompletedV1>(), c => c.OrderId == order.Id);   // jen jednou
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
