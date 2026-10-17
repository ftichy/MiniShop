using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Inventory;
using MiniShop.Application.Ordering;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Ordering.EventHandlers;
using MiniShop.Application.Ordering.IntegrationEvents;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Contracts;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Application;

/// <summary>
/// Testy OrderService a ságy objednávky nad skutečnou SQLite databází v paměti.
/// Skládají stejné služby jako aplikace (handlery, orchestrátor ságy, modul Inventory) a každá akce běží
/// ve vlastním scope jako HTTP požadavek. Integrační zprávy doručuje InProcessIntegrationBus (bez RabbitMQ).
/// Handlery neběží hned – test po akci "zavolá pošťáka" (<see cref="ProcessOutboxAsync"/>), jako OutboxWorker.
/// </summary>
public sealed class OrderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ReservationTimeout = TimeSpan.FromSeconds(30);

    private readonly InMemoryDatabase _orderingDatabase = new();
    private readonly InMemoryDatabase _inventoryDatabase = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly RecordingOrderNotifier _notifier = new();
    private readonly List<IDomainEvent> _dispatched = new();
    private readonly ServiceProvider _services;
    private readonly RecordingIntegrationBus _bus;

    public OrderServiceTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(_time);
        services.AddInfrastructure(_orderingDatabase.ConnectionString, _inventoryDatabase.ConnectionString);

        // Stejné služby a handlery jako v aplikaci (ApplicationServiceCollectionExtensions).
        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton<IDiscountPolicy>(new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m));
        services.AddDomainEventHandler<OrderConfirmed, SendOrderConfirmationHandler>();
        services.AddDomainEventHandler<OrderRejected, SendRejectionNoticeHandler>();
        services.AddDomainEventHandler<OrderCompleted, SendCompletionNoticeHandler>();
        services.AddDomainEventHandler<OrderCancelled, SendCancellationNoticeHandler>();
        services.AddDomainEventHandler<OrderPlaced, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCompleted, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCancelled, PublishOrderIntegrationEventsHandler>();

        // Sága (orchestrátor), kopie katalogu (krok 10) a modul Inventory.
        services.AddSingleton(new OrderSagaSettings(ReservationTimeout));
        services.AddDomainEventHandler<OrderPlaced, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCompleted, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCancelled, OrderSagaOrchestrator>();
        services.AddIntegrationMessageHandler<StockReservedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReservationFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReleasedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductUpdatedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductRemovedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddScoped<IProductService, ProductService>();
        services.AddIntegrationMessageHandler<ReserveStockV1, ReserveStockHandler>(IntegrationModules.Inventory);
        services.AddIntegrationMessageHandler<ReleaseStockV1, ReleaseStockHandler>(IntegrationModules.Inventory);
        services.AddIntegrationMessageHandler<SyncCatalogV1, SyncCatalogHandler>(IntegrationModules.Inventory);
        services.AddScoped<CatalogSyncService>();

        // Testovací náhrady: notifikace do seznamu, "broker" si zprávy zapamatuje a doručí je v procesu,
        // skutečný dispatcher obalený záznamníkem.
        services.AddSingleton<IOrderNotifier>(_notifier);
        services.AddSingleton<IIntegrationBus>(sp =>
            new RecordingIntegrationBus(ActivatorUtilities.CreateInstance<InProcessIntegrationBus>(sp)));
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<IDomainEventDispatcher>(sp =>
            new RecordingDomainEventDispatcher(_dispatched, sp.GetRequiredService<DomainEventDispatcher>()));

        _services = services.BuildServiceProvider();
        _bus = (RecordingIntegrationBus)_services.GetRequiredService<IIntegrationBus>();
        OrderingDb(db => db.Database.EnsureCreatedAsync()).GetAwaiter().GetResult();
        InventoryDb(db => db.Database.EnsureCreatedAsync()).GetAwaiter().GetResult();
    }

    // ---------- Vytvoření objednávky ----------

    [Fact]
    public async Task PlaceOrder_PredZpracovanimOutboxu_JePendingASkladJeBezeZmeny()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 3))));

        // Krok 9: objednávka je jen přijatá, sklad se zatím nezměnil – o rezervaci rozhodne sága.
        Assert.Equal("Pending", order.Status);
        Assert.Equal(300m, order.TotalPrice);
        Assert.Equal(Now.UtcDateTime, order.CreatedAtUtc);
        Assert.Equal(5, await StockAsync());
        Assert.Empty(_notifier.Events);
    }

    [Fact]
    public async Task PlaceOrder_SkladZarezervuje_ObjednavkaPotvrzena()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        Assert.Equal(2, await StockAsync());
        Assert.Contains($"confirmed:{order.Id}", _notifier.Events);

        var saga = await SagaAsync(order.Id);
        Assert.Equal(OrderSagaState.Reserved, saga.State);
        var reservation = Assert.Single(await InventoryDb(db => db.StockReservations.ToListAsync()));
        Assert.Equal((order.Id, product.Id, 3), (reservation.OrderId, reservation.ProductId, reservation.Quantity));
    }

    [Fact]
    public async Task PlaceOrder_PrubehSagy_PrikazAOdpovedSkladu()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 2))));
        await ProcessOutboxAsync();

        // Orchestrátor poslal skladu příkaz, sklad odpověděl událostí.
        var reserve = Assert.Single(_bus.Published.OfType<ReserveStockV1>());
        Assert.Equal(order.Id, reserve.OrderId);
        Assert.Equal(new StockLineV1(product.Id, 2), Assert.Single(reserve.Lines));
        Assert.Equal(order.Id, Assert.Single(_bus.Published.OfType<StockReservedV1>()).OrderId);
        Assert.Empty(_bus.Published.OfType<StockReservationFailedV1>());
    }

    [Fact]
    public async Task PlaceOrder_NedostatekZbozi_ObjednavkaZamitnutaSDuvodem()
    {
        var product = await AddProductAsync(price: 100m, stock: 2);

        // Krok 9: žádná výjimka (dřív InsufficientStockException → 400), objednávka se přijme…
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        // …a sága ji zamítne.
        var rejected = await Orders(s => s.GetByIdAsync(order.Id));
        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("požadováno 3 ks, skladem 2 ks", rejected.RejectionReason);
        Assert.Equal(2, await StockAsync());
        Assert.Empty(await InventoryDb(db => db.StockReservations.ToListAsync()));
        Assert.Contains($"rejected:{order.Id}", _notifier.Events);
        Assert.Equal(OrderSagaState.Rejected, (await SagaAsync(order.Id)).State);
    }

    [Fact]
    public async Task PlaceOrder_VicePolozek_RezervujeVsechnoNeboNic()
    {
        var plenty = await AddProductAsync(price: 100m, stock: 50);
        var scarce = await AddProductAsync(price: 100m, stock: 1);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((plenty.Id, 5), (scarce.Id, 2))));
        await ProcessOutboxAsync();

        // Jedna položka chybí → nezarezervuje se ani ta druhá.
        Assert.Equal("Rejected", await StatusAsync(order.Id));
        Assert.Equal(50, await StockAsync(plenty.Id));
        Assert.Equal(1, await StockAsync(scarce.Id));
    }

    [Fact]
    public async Task PlaceOrder_Od10Kusu_UplatniMnozstevniSlevu()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 10))));

        Assert.Equal(100m, order.Items.Single().Discount);
        Assert.Equal(900m, order.TotalPrice);
    }

    [Fact]
    public async Task PlaceOrder_StejnyProduktVicekrat_SeSlouciDoJednePolozky()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 4), (product.Id, 6))));

        var item = Assert.Single(order.Items);
        Assert.Equal(10, item.Quantity);
    }

    [Fact]
    public async Task PlaceOrder_NeexistujiciProdukt_VyhodiNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Orders(s => s.PlaceOrderAsync(CreateRequest((999, 1)))));
    }

    // ---------- Zrušení a vyřízení ----------

    [Fact]
    public async Task Cancel_PotvrzenouObjednavku_SklademSeVratiPresKompenzaci()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 5);
        Assert.Equal(0, await StockAsync());

        var cancelled = await Orders(s => s.CancelAsync(order.Id));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(0, await StockAsync());   // eventual consistency: zboží se ještě nevrátilo

        await ProcessOutboxAsync();

        Assert.Equal(5, await StockAsync());
        Assert.Contains($"cancelled:{order.Id}", _notifier.Events);
        Assert.Single(_bus.Published.OfType<ReleaseStockV1>());
        Assert.Single(_bus.Published.OfType<StockReleasedV1>());
        Assert.Equal(OrderSagaState.Released, (await SagaAsync(order.Id)).State);
        Assert.All(await InventoryDb(db => db.StockReservations.ToListAsync()), r => Assert.NotNull(r.ReleasedAtUtc));
    }

    [Fact]
    public async Task Cancel_CekajiciObjednavku_VyhodiDomainException()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id)));
    }

    [Fact]
    public async Task Cancel_VyrizenouObjednavku_VyhodiDomainException()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await Orders(s => s.CompleteAsync(order.Id));

        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id)));
    }

    [Fact]
    public async Task Complete_PotvrzenouObjednavku_ZboziZustaneProdane()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();

        Assert.Contains($"completed:{order.Id}", _notifier.Events);
        Assert.Equal(4, await StockAsync());
        Assert.Equal(OrderSagaState.Completed, (await SagaAsync(order.Id)).State);
    }

    [Fact]
    public async Task Cancel_NotifikacePoprveSelze_PriOpakovaniSeZboziNevratiDvakrat()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 2);
        _notifier.CancellationFailuresLeft = 1;

        await Orders(s => s.CancelAsync(order.Id));

        // 1. kolo workera: orchestrátor pošle ReleaseStock, notifikace selže → OrderCancelled zůstane nezpracovaná.
        await ProcessOneRoundAsync();
        Assert.DoesNotContain($"cancelled:{order.Id}", _notifier.Events);

        // Další kola: OrderCancelled se zopakuje – orchestrátor ji díky inboxu přeskočí, notifikace projde.
        await ProcessOutboxAsync();
        Assert.Equal(5, await StockAsync());   // bez inboxu by šly dva příkazy ReleaseStock
        Assert.Contains($"cancelled:{order.Id}", _notifier.Events);
        Assert.Single(_bus.Published.OfType<ReleaseStockV1>());

        var message = await OrderingDb(db => db.OutboxMessages.SingleAsync(m => m.Type == "OrderCancelled"));
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Equal(1, message.Attempts);
    }

    // ---------- Timeout a pozdní odpověď ----------

    [Fact]
    public async Task Timeout_SkladNeodpovedel_ObjednavkaZamitnutaAPozdniRezervaceSeUvolni()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 2))));

        // 1. kolo: sága založena, ReserveStockV1 čeká v outboxu – "sklad neběží".
        await ProcessOneRoundAsync();
        Assert.Empty(await ExpiredSagasAsync());

        // Uplyne termín → timeout worker objednávku zamítne.
        _time.Advance(ReservationTimeout + TimeSpan.FromSeconds(1));
        var expired = Assert.Single(await ExpiredSagasAsync());
        Assert.True(await Saga(o => o.TimeOutAsync(expired, CancellationToken.None)));

        var rejected = await Orders(s => s.GetByIdAsync(order.Id));
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(OrderSaga.TimeoutReason, rejected.RejectionReason);

        // Sklad se "probudí": zarezervuje, sága zjistí, že je pozdě, a pošle ReleaseStock (kompenzace).
        await ProcessOutboxAsync();

        Assert.Single(_bus.Published.OfType<StockReservedV1>());
        Assert.Single(_bus.Published.OfType<ReleaseStockV1>());
        Assert.Equal(5, await StockAsync());                      // zboží je zpátky
        Assert.Equal("Rejected", await StatusAsync(order.Id));    // objednávka zůstala zamítnutá
        Assert.Equal(OrderSagaState.Released, (await SagaAsync(order.Id)).State);
        Assert.Contains($"rejected:{order.Id}", _notifier.Events);
        Assert.DoesNotContain($"confirmed:{order.Id}", _notifier.Events);
    }

    [Fact]
    public async Task Timeout_SkladOdpovedelVcas_NicSeNestane()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        _time.Advance(ReservationTimeout + TimeSpan.FromSeconds(1));

        Assert.Empty(await ExpiredSagasAsync());
        Assert.False(await Saga(o => o.TimeOutAsync(order.Id, CancellationToken.None)));
        Assert.Equal("Confirmed", await StatusAsync(order.Id));
    }

    [Fact]
    public async Task Saga_DuplicitniOdpovedSkladu_SeIgnoruje()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        // Stejná odpověď s jiným ID (inbox ji nepozná) – zachytí ji stavový automat ságy.
        await _bus.PublishAsync(new StockReservedV1(Guid.NewGuid(), Now.UtcDateTime, order.Id));
        await ProcessOutboxAsync();

        Assert.Single(_notifier.Events, e => e == $"confirmed:{order.Id}");
        Assert.Equal("Confirmed", await StatusAsync(order.Id));
    }

    // ---------- Integrační události z kroku 7 ----------

    [Fact]
    public async Task PlaceOrder_WorkerPublikujeOrderPlacedV1SDatyObjednavky()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        var placed = Assert.Single(_bus.Published.OfType<OrderPlacedV1>());
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal("jan@example.com", placed.CustomerEmail);
        Assert.Equal(300m, placed.TotalPrice);
        Assert.Equal(Now.UtcDateTime, placed.OccurredAtUtc);   // kdy se fakt stal, ne kdy odešla zpráva
        Assert.Equal(new OrderLineV1(product.Id, 3), Assert.Single(placed.Lines));
    }

    [Fact]
    public async Task PlaceOrder_IntegracniZpravyJsouVOutboxuJakoSamostatneZpravy()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        // 1. kolo: handlery OrderPlaced zapíšou do outboxu integrační událost i příkaz pro sklad – ven ještě neodešly.
        await ProcessOneRoundAsync();
        var integration = await OrderingDb(db => db.OutboxMessages.Where(m => m.Kind == OutboxMessageKind.Integration).ToListAsync());
        Assert.Equal(new[] { "OrderPlacedV1", "ReserveStockV1" }, integration.Select(m => m.Type).Order());
        Assert.All(integration, m => Assert.Null(m.ProcessedAtUtc));
        Assert.Empty(_bus.Published);

        // 2. kolo: worker je pošle přes bus. Doménové handlery je nedostanou.
        // (V tomtéž kole stihne pošťák skladu poslat i odpověď StockReservedV1 – krok 10: každý modul má svůj outbox.)
        await ProcessOneRoundAsync();
        Assert.Single(_bus.Published.OfType<OrderPlacedV1>());
        Assert.Single(_bus.Published.OfType<ReserveStockV1>());
        Assert.Single(_dispatched);
    }

    [Fact]
    public async Task CancelAComplete_WorkerPublikujeIntegracniUdalosti()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var cancelledOrder = await PlaceConfirmedOrderAsync(product.Id, quantity: 2);
        var completedOrder = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        await Orders(s => s.CancelAsync(cancelledOrder.Id));
        await Orders(s => s.CompleteAsync(completedOrder.Id));
        await ProcessOutboxAsync();

        var cancelled = Assert.Single(_bus.Published.OfType<OrderCancelledV1>());
        Assert.Equal(cancelledOrder.Id, cancelled.OrderId);
        Assert.Equal(new OrderLineV1(product.Id, 2), Assert.Single(cancelled.Lines));

        var completed = Assert.Single(_bus.Published.OfType<OrderCompletedV1>());
        Assert.Equal(completedOrder.Id, completed.OrderId);
        Assert.Equal("jan@example.com", completed.CustomerEmail);

        Assert.Equal(2, _bus.Published.OfType<OrderPlacedV1>().Count());
    }

    [Fact]
    public async Task VsechnyZpravyVOutboxu_JsouPoZpracovaniOznacene()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 2);
        await Orders(s => s.CancelAsync(order.Id));

        await ProcessOutboxAsync();

        // Krok 10: každý modul má vlastní outbox – zprávy odchází z databáze toho, kdo je poslal.
        var ordering = await OrderingDb(db => db.OutboxMessages.ToListAsync());
        Assert.All(ordering, m => Assert.Equal(Now.UtcDateTime, m.ProcessedAtUtc));
        Assert.Equal(
            new[]
            {
                "OrderCancelled", "OrderCancelledV1", "OrderConfirmed", "OrderPlaced", "OrderPlacedV1",
                "ReleaseStockV1", "ReserveStockV1"
            },
            ordering.Select(m => m.Type).Order());

        var inventory = await InventoryDb(db => db.OutboxMessages.ToListAsync());
        Assert.All(inventory, m => Assert.Equal(Now.UtcDateTime, m.ProcessedAtUtc));
        Assert.Equal(new[] { "ProductUpdatedV1", "StockReleasedV1", "StockReservedV1" }, inventory.Select(m => m.Type).Order());
    }

    // ---------- Krok 10: kopie katalogu v Orderingu ----------

    [Fact]
    public async Task NovyProdukt_JdeObjednatAzPoPromitnutiDoKatalogu()
    {
        var product = await Products(s => s.CreateAsync(new CreateProductRequest { Name = "Novinka", Price = 250m, StockQuantity = 5 }));

        // ProductUpdatedV1 ještě čeká v outboxu skladu → Ordering o produktu neví (eventual consistency).
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1)))));

        await ProcessOutboxAsync();

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));
        Assert.Equal("Novinka", order.Items.Single().ProductName);
        Assert.Equal(250m, order.TotalPrice);
    }

    [Fact]
    public async Task ZmenaCeny_NoveObjednavkyMajiNovouCenu_StareSeNemeni()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);
        var before = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        await Products(s => s.UpdateAsync(product.Id, new UpdateProductRequest { Name = "Testovací produkt", Price = 150m }));
        await ProcessOutboxAsync();
        var after = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        Assert.Equal(150m, after.TotalPrice);
        Assert.Equal(100m, (await Orders(s => s.GetByIdAsync(before.Id))).TotalPrice);   // snímek ceny v položce
        Assert.Equal(150m, (await OrderingDb(db => db.CatalogProducts.SingleAsync())).Price);
    }

    [Fact]
    public async Task SmazanyProdukt_NejdeObjednat()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        await Products(async s => { await s.DeleteAsync(product.Id); return true; });
        await ProcessOutboxAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1)))));
        Assert.True((await OrderingDb(db => db.CatalogProducts.SingleAsync())).IsRemoved);
    }

    // ---------- Obnova kopie katalogu (SyncCatalogV1) ----------

    [Fact]
    public async Task ZtracenaKopieKatalogu_ObnoviSeZeSkladu()
    {
        var product = await AddProductAsync(price: 120m, stock: 5);

        // Simulace smazané ordering.db: sklad produkt má, kopie katalogu je prázdná a sama se nedoplní.
        await OrderingDb(db => db.CatalogProducts.ExecuteDeleteAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1)))));

        Assert.True(await CatalogSync(s => s.RequestIfEmptyAsync()));
        await ProcessOutboxAsync();   // SyncCatalogV1 → sklad → ProductUpdatedV1 → projekce

        var catalog = await Orders(s => s.GetCatalogAsync());
        Assert.Equal(new CatalogItemDto(product.Id, "Testovací produkt", 120m), Assert.Single(catalog));
        Assert.Equal(120m, (await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))))).TotalPrice);
    }

    [Fact]
    public async Task PlnaKopieKatalogu_PoStartuSeSkladuNeptaSe()
    {
        await AddProductAsync(price: 120m, stock: 5);
        _bus.Published.Clear();

        Assert.False(await CatalogSync(s => s.RequestIfEmptyAsync()));
        await ProcessOutboxAsync();

        Assert.Empty(_bus.Published);
    }

    /// <summary>
    /// Obnova posílá skutečný čas poslední změny produktu, ne "teď". Kdyby nesla "teď", přebila by novější změnu,
    /// která je k Orderingu zrovna na cestě. Zároveň má každá zpráva nové EventId – inbox ji nesmí zahodit.
    /// </summary>
    [Fact]
    public async Task ObnovaKatalogu_NeseCasPosledniZmenyANoveEventId()
    {
        var product = await AddProductAsync(price: 120m, stock: 5);   // AddProductAsync vyčistí _bus.Published
        _time.Advance(TimeSpan.FromHours(1));

        await CatalogSync(async s => { await s.RequestAsync(); return true; });
        await ProcessOutboxAsync();

        Assert.Single(_bus.Published.OfType<SyncCatalogV1>());
        var resent = Assert.Single(_bus.Published.OfType<ProductUpdatedV1>());
        Assert.Equal(product.Id, resent.ProductId);
        Assert.Equal(Now.UtcDateTime, resent.OccurredAtUtc);   // čas založení produktu, ne "o hodinu později"

        // Obě odeslání (založení i obnova) jsou v outboxu skladu jako dvě různé zprávy.
        var sentIds = await InventoryDb(db => db.OutboxMessages
            .Where(m => m.Type == nameof(ProductUpdatedV1)).Select(m => m.Id).ToListAsync());
        Assert.Equal(2, sentIds.Distinct().Count());
        Assert.Contains(resent.EventId, sentIds);
    }

    [Fact]
    public async Task Moduly_MajiOddeleneDatabaze()
    {
        // Ordering nezná tabulky skladu a naopak – jediná cesta mezi nimi jsou zprávy.
        Assert.Null(await OrderingDb(db => Task.FromResult(db.Model.FindEntityType(typeof(MiniShop.Domain.Inventory.Product)))));
        Assert.Null(await InventoryDb(db => Task.FromResult(db.Model.FindEntityType(typeof(MiniShop.Domain.Orders.Order)))));
        Assert.NotEqual(_orderingDatabase.ConnectionString, _inventoryDatabase.ConnectionString);
    }

    // ---------- Krok 11: jedna trasa přes celou ságu ----------

    [Fact]
    public async Task PlaceOrder_CelaSagaJeVJedneTrase()
    {
        using var recorder = new ActivityRecorder();
        var product = await AddProductAsync(price: 100m, stock: 5);

        // Simulace HTTP požadavku "POST /api/orders" – v aplikaci tuhle aktivitu založí ASP.NET Core.
        var request = new Activity("POST /api/orders").Start();
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 2))));
        request.Stop();

        // Worker běží mimo požadavek (bez Activity.Current) – trasu musí navázat podle TraceParent v outboxu.
        Assert.Null(Activity.Current);
        await ProcessOutboxAsync();
        Assert.Equal("Confirmed", await StatusAsync(order.Id));

        var spans = recorder.SpansOf(request.TraceId).Select(a => a.DisplayName).ToList();
        string[] expected =
        [
            "dispatch OrderPlaced",            // Ordering: doménová událost → orchestrátor ságy
            "publish ReserveStockV1",          // Ordering → "broker"
            "process ReserveStockV1",          // Inventory: rezervace
            "publish StockReservedV1",         // Inventory → "broker"
            "process StockReservedV1",         // Ordering: sága potvrdí objednávku
            "dispatch OrderConfirmed"          // Ordering: notifikace o potvrzení
        ];
        Assert.All(expected, name => Assert.Contains(name, spans));

        // Kroky ságy jdou po sobě: každý začne až po tom, který ho vyvolal.
        var indexes = expected.Select(name => spans.IndexOf(name)).ToList();
        Assert.Equal(indexes.Order(), indexes);

        // Všechny zprávy objednávky v outboxech OBOU modulů nesou trace ID původního požadavku.
        var messages = (await OrderingDb(db => db.OutboxMessages.ToListAsync()))
            .Concat(await InventoryDb(db => db.OutboxMessages.ToListAsync()))
            .Where(m => !m.Type.StartsWith("Product"))   // zprávy z přípravy dat vznikly mimo požadavek
            .ToList();
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Contains(request.TraceId.ToHexString(), m.TraceParent));
    }

    [Fact]
    public async Task PlaceOrder_MimoSledovanouOperaci_ZpravyBezTraceParent()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        var placed = await OrderingDb(db => db.OutboxMessages.SingleAsync(m => m.Type == nameof(OrderPlaced)));
        Assert.Null(placed.TraceParent);
    }

    // ---------- Pomocné metody ----------

    /// <summary>
    /// Zpracuje outboxy všech modulů jako několik kol OutboxWorkeru: pokračuje, dokud byla dávka plná nebo
    /// handlery přidaly nové zprávy (sága = řetěz zpráv). Pozor: v dalších kolech se zkusí znovu
    /// i zprávy, které selhaly – pro jedno kolo je <see cref="ProcessOneRoundAsync"/>.
    /// </summary>
    private async Task ProcessOutboxAsync()
    {
        bool again;
        do
        {
            var before = await OutboxCountAsync();
            var processed = await ProcessOneRoundAsync();
            again = processed >= OutboxProcessor.BatchSize || await OutboxCountAsync() > before;
        }
        while (again);
    }

    /// <summary>Právě jedno kolo workera: jedna dávka z outboxu každého modulu (krok 10).</summary>
    private async Task<int> ProcessOneRoundAsync()
    {
        var processed = 0;
        foreach (var processor in _services.GetServices<IOutboxProcessor>())
        {
            processed += await processor.ProcessPendingAsync();
        }

        return processed;
    }

    private async Task<int> OutboxCountAsync() =>
        await OrderingDb(db => db.OutboxMessages.CountAsync()) + await InventoryDb(db => db.OutboxMessages.CountAsync());

    private async Task<OrderDto> PlaceConfirmedOrderAsync(int productId, int quantity)
    {
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((productId, quantity))));
        await ProcessOutboxAsync();
        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        return order;
    }

    /// <summary>Zavolá OrderService ve vlastním scope – jako jeden HTTP požadavek.</summary>
    private async Task<T> Orders<T>(Func<IOrderService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IOrderService>());
    }

    /// <summary>Zavolá orchestrátor ve vlastním scope – jako OrderSagaTimeoutWorker.</summary>
    private async Task<T> Saga<T>(Func<OrderSagaOrchestrator, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>());
    }

    /// <summary>Přímý přístup k databázi modulu Ordering ve vlastním scope (čerstvý DbContext, žádná data z cache).</summary>
    private async Task<T> OrderingDb<T>(Func<OrderingDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<OrderingDbContext>());
    }

    /// <summary>Přímý přístup k databázi modulu Inventory.</summary>
    private async Task<T> InventoryDb<T>(Func<InventoryDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<InventoryDbContext>());
    }

    /// <summary>Zavolá obnovu katalogu ve vlastním scope – jako CatalogSyncOnStartupWorker nebo tlačítko v UI.</summary>
    private async Task<T> CatalogSync<T>(Func<CatalogSyncService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CatalogSyncService>());
    }

    /// <summary>Zavolá správu produktů (modul Inventory) ve vlastním scope.</summary>
    private async Task<T> Products<T>(Func<IProductService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IProductService>());
    }

    private async Task<IReadOnlyList<int>> ExpiredSagasAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOrderSagaRepository>()
            .GetExpiredOrderIdsAsync(_time.GetUtcNow().UtcDateTime);
    }

    private async Task<string> StatusAsync(int orderId) => (await Orders(s => s.GetByIdAsync(orderId))).Status;

    private Task<OrderSaga> SagaAsync(int orderId) => OrderingDb(db => db.OrderSagas.SingleAsync(s => s.OrderId == orderId));

    private Task<int> StockAsync() => InventoryDb(async db => (await db.Products.SingleAsync()).StockQuantity);

    private Task<int> StockAsync(int productId) =>
        InventoryDb(async db => (await db.Products.SingleAsync(p => p.Id == productId)).StockQuantity);

    /// <summary>
    /// Založí produkt ve skladu a nechá ho promítnout do kopie katalogu v Orderingu (krok 10) –
    /// teprve pak jde objednat. Zprávy z přípravy dat se z "brokeru" smažou, ať nepletou testy.
    /// </summary>
    private async Task<ProductDto> AddProductAsync(decimal price, int stock)
    {
        var product = await Products(s => s.CreateAsync(new CreateProductRequest
        {
            Name = "Testovací produkt",
            Price = price,
            StockQuantity = stock
        }));
        await ProcessOutboxAsync();
        _bus.Published.Clear();
        return product;
    }

    private static CreateOrderRequest CreateRequest(params (int ProductId, int Quantity)[] items) => new()
    {
        CustomerName = "Jan Novák",
        CustomerEmail = "jan@example.com",
        Items = items.Select(i => new CreateOrderItemRequest { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
    };

    public void Dispose()
    {
        _services.Dispose();
        _orderingDatabase.Dispose();
        _inventoryDatabase.Dispose();
    }
}
