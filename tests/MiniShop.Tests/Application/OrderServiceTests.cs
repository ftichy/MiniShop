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
using MiniShop.Application.Ordering.Carts;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Ordering.Checkout;
using MiniShop.Application.Ordering.EventHandlers;
using MiniShop.Application.Ordering.IntegrationEvents;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Contracts;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
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

    /// <summary>E-shop krok 2: kategorie „Nezařazeno“ – zakládá ji migrace inventory.db (pevné ID 1).</summary>
    private const int UncategorizedId = 1;

    private static readonly TimeSpan ReservationTimeout = TimeSpan.FromSeconds(30);

    private readonly InMemoryDatabase _orderingDatabase = new();
    private readonly InMemoryDatabase _inventoryDatabase = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly RecordingOrderNotifier _notifier = new();
    private readonly InMemoryProductImageStore _images = new();   // e-shop krok 2B
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
        services.AddIntegrationMessageHandler<ProductUpdatedV2, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockAvailabilityChangedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductRemovedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddSingleton<IProductImageStore>(_images);
        services.AddScoped<IProductImageService, ProductImageService>();
        services.AddScoped<ICatalogQueryService, CatalogQueryService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddSingleton<IShippingPriceCalculator>(new StandardShippingPriceCalculator(courierPrice: 99m, freeFrom: 1_000m));
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
        OrderingDatabaseInitializer.InitializeAsync(_services).GetAwaiter().GetResult();
        InventoryDb(async db => { await InventoryDatabaseInitializer.MigrateAsync(db); return true; }).GetAwaiter().GetResult();
    }

    // ---------- Vytvoření objednávky ----------

    [Fact]
    public async Task PlaceOrder_PredZpracovanimOutboxu_JePendingASkladJeBezeZmeny()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 3))));

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

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 3))));
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

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 2))));
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
        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        // …a sága ji zamítne.
        var rejected = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
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

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((plenty.Id, 5), (scarce.Id, 2))));
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

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 10))));

        Assert.Equal(100m, order.Items.Single().Discount);
        Assert.Equal(900m, order.TotalPrice);
    }

    [Fact]
    public async Task PlaceOrder_StejnyProduktVicekrat_SeSlouciDoJednePolozky()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 4), (product.Id, 6))));

        var item = Assert.Single(order.Items);
        Assert.Equal(10, item.Quantity);
    }

    [Fact]
    public async Task PlaceOrder_NeexistujiciProdukt_VyhodiNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Orders(s => PlaceAsJan(s, CreateRequest((999, 1)))));
    }

    // ---------- Účty krok 3: vlastník objednávky ----------

    [Fact]
    public async Task PlaceOrder_ZakaznikJeZUctu_JmenoAEmailJsouKopie()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1)), Petra));

        Assert.Equal("Petra Malá", order.CustomerName);
        Assert.Equal("petra@example.com", order.CustomerEmail);
        var stored = await OrderingDb(db => db.Orders.SingleAsync(o => o.Id == order.Id));
        Assert.Equal("zakaznik-petra", stored.CustomerId);
    }

    [Fact]
    public async Task Seznam_ZakaznikVidiJenSve_SpravceVsechny()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var jans = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1)), Jan));
        var petras = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1)), Petra));

        var forJan = await Orders(s => s.GetAllAsync(OrderAccess.Customer(Jan.CustomerId)));
        var forAdmin = await Orders(s => s.GetAllAsync(OrderAccess.AllOrders));

        Assert.Equal([jans.Id], forJan.Select(o => o.Id));
        Assert.Equal([petras.Id, jans.Id], forAdmin.Select(o => o.Id));
    }

    [Fact]
    public async Task CiziObjednavka_DetailIZruseni_SeTvariJakoNeexistujici()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var petras = await PlaceConfirmedOrderAsync(product.Id, quantity: 1, customer: Petra);
        var asJan = OrderAccess.Customer(Jan.CustomerId);

        // Stejná výjimka i hláška jako u neexistující objednávky → 404, Jan nepozná, že objednávka existuje.
        var detail = await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => s.GetByIdAsync(petras.Id, asJan)));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => s.GetByIdAsync(999, asJan)));
        Assert.Equal(missing.Message.Replace("999", petras.Id.ToString()), detail.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => s.CancelAsync(petras.Id, asJan)));

        Assert.Equal("Confirmed", await StatusAsync(petras.Id));   // nic se nezměnilo
        var own = await Orders(s => s.CancelAsync(petras.Id, OrderAccess.Customer(Petra.CustomerId)));
        Assert.Equal("Cancelled", own.Status);
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => s.GetDetailAsync(petras.Id, asJan)));   // e-shop krok 5
    }

    // ---------- E-shop krok 5: historie stavů ----------

    [Fact]
    public async Task Detail_HistorieCeleCestyPresSaguAZruseni()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);   // sága potvrdí přes outbox
        await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));

        var detail = await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));

        Assert.Equal(order.Id, detail.Order.Id);
        Assert.Equal(["Pending", "Confirmed", "Cancelled"], detail.History.Select(h => h.Status));
        Assert.All(detail.History, h => Assert.Equal(DateTimeKind.Utc, h.ChangedAtUtc!.Value.Kind));
    }

    [Fact]
    public async Task Detail_ZamitnutaObjednavka_HistorieMaDuvod()
    {
        var product = await AddProductAsync(price: 100m, stock: 2);
        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        var detail = await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.AllOrders));

        Assert.Equal(["Pending", "Rejected"], detail.History.Select(h => h.Status));
        Assert.Contains("požadováno 3 ks, skladem 2 ks", detail.History[1].Note);
    }

    // ---------- Zrušení a vyřízení ----------

    [Fact]
    public async Task Cancel_PotvrzenouObjednavku_SklademSeVratiPresKompenzaci()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 5);
        Assert.Equal(0, await StockAsync());

        var cancelled = await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));
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
        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1))));

        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders)));
    }

    [Fact]
    public async Task Cancel_VyrizenouObjednavku_VyhodiDomainException()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await Orders(s => s.CompleteAsync(order.Id));

        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders)));
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

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));

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
        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 2))));

        // 1. kolo: sága založena, ReserveStockV1 čeká v outboxu – "sklad neběží".
        await ProcessOneRoundAsync();
        Assert.Empty(await ExpiredSagasAsync());

        // Uplyne termín → timeout worker objednávku zamítne.
        _time.Advance(ReservationTimeout + TimeSpan.FromSeconds(1));
        var expired = Assert.Single(await ExpiredSagasAsync());
        Assert.True(await Saga(o => o.TimeOutAsync(expired, CancellationToken.None)));

        var rejected = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
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

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 3))));
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
        await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1))));

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

        await Orders(s => s.CancelAsync(cancelledOrder.Id, OrderAccess.AllOrders));
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
        await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));

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
        Assert.Equal(new[] { "ProductUpdatedV1", "ProductUpdatedV2", "StockReleasedV1", "StockReservedV1" },
            inventory.Select(m => m.Type).Order());
    }

    // ---------- Krok 10: kopie katalogu v Orderingu ----------

    [Fact]
    public async Task NovyProdukt_JdeObjednatAzPoPromitnutiDoKatalogu()
    {
        var product = await Products(s => s.CreateAsync(new CreateProductRequest { Name = "Novinka", Price = 250m, StockQuantity = 5, CategoryId = UncategorizedId }));

        // ProductUpdatedV1 ještě čeká v outboxu skladu → Ordering o produktu neví (eventual consistency).
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1)))));

        await ProcessOutboxAsync();

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1))));
        Assert.Equal("Novinka", order.Items.Single().ProductName);
        Assert.Equal(250m, order.TotalPrice);
    }

    [Fact]
    public async Task ZmenaCeny_NoveObjednavkyMajiNovouCenu_StareSeNemeni()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);
        var before = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        await Products(s => s.UpdateAsync(product.Id, new UpdateProductRequest { Name = "Testovací produkt", Price = 150m, CategoryId = UncategorizedId }));
        await ProcessOutboxAsync();
        var after = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1))));

        Assert.Equal(150m, after.TotalPrice);
        Assert.Equal(100m, (await Orders(s => s.GetByIdAsync(before.Id, OrderAccess.AllOrders))).TotalPrice);   // snímek ceny v položce
        Assert.Equal(150m, (await OrderingDb(db => db.CatalogProducts.SingleAsync())).Price);
    }

    [Fact]
    public async Task SmazanyProdukt_NejdeObjednat()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        await Products(async s => { await s.DeleteAsync(product.Id); return true; });
        await ProcessOutboxAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1)))));
        Assert.True((await OrderingDb(db => db.CatalogProducts.SingleAsync())).IsRemoved);
    }

    // ---------- E-shop krok 2: výloha (ProductUpdatedV2, dostupnost, kategorie, hledání) ----------

    [Fact]
    public async Task NovyProdukt_SkladPosleV1IV2_KopieMaPopisKategoriiADostupnost()
    {
        var product = await Products(s => s.CreateAsync(new CreateProductRequest
        {
            Name = "Myš", Description = "Bezdrátová", Price = 890m, StockQuantity = 40, CategoryId = UncategorizedId
        }));
        await ProcessOutboxAsync();

        // Expand → contract: stejná změna ve dvou verzích, každá s vlastním ID (inbox by jinak V2 zahodil jako duplicitu).
        var v1 = Assert.Single(_bus.Published.OfType<ProductUpdatedV1>());
        var v2 = Assert.Single(_bus.Published.OfType<ProductUpdatedV2>());
        Assert.NotEqual(v1.EventId, v2.EventId);

        var item = Assert.Single(await Orders(s => s.GetCatalogAsync()));
        Assert.Equal(new CatalogItemDto(product.Id, "Myš", "Bezdrátová", 890m, UncategorizedId, "Nezařazeno", "InStock"), item);
    }

    [Fact]
    public async Task Rezervace_ZmeniPasmoDostupnosti_KopieKataloguToVi()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);   // 5 ks = poslední kusy
        Assert.Equal("LowStock", (await Orders(s => s.GetCatalogAsync())).Single().Availability);

        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 5);   // rezervace → 0 ks

        Assert.Single(_bus.Published.OfType<StockAvailabilityChangedV1>());
        Assert.Equal("OutOfStock", (await Orders(s => s.GetCatalogAsync())).Single().Availability);

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));
        await ProcessOutboxAsync();   // kompenzace vrátí zboží → zpět poslední kusy

        Assert.Equal("LowStock", (await Orders(s => s.GetCatalogAsync())).Single().Availability);
    }

    [Fact]
    public async Task ZmenaZasobyVeStejnemPasmu_ZadnaZprava()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);

        await PlaceConfirmedOrderAsync(product.Id, quantity: 3);   // 47 ks – pořád skladem

        Assert.Empty(_bus.Published.OfType<StockAvailabilityChangedV1>());
    }

    [Fact]
    public async Task PrejmenovaniKategorie_VylohaMaNoveJmeno()
    {
        var category = await Categories(s => s.CreateAsync(new CategoryRequest { Name = "Periferie" }));
        var product = await Products(s => s.CreateAsync(new CreateProductRequest
        {
            Name = "Myš", Price = 890m, StockQuantity = 40, CategoryId = category.Id
        }));
        await ProcessOutboxAsync();
        _time.Advance(TimeSpan.FromMinutes(1));

        await Categories(s => s.RenameAsync(category.Id, new CategoryRequest { Name = "Příslušenství" }));
        await ProcessOutboxAsync();

        var item = Assert.Single(await Orders(s => s.GetCatalogAsync()));
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal("Příslušenství", item.CategoryName);
    }

    [Fact]
    public async Task Kategorie_SProduktyNejdeSmazat_NazevJeUnikatni()
    {
        await AddProductAsync(price: 100m, stock: 5);   // v kategorii „Nezařazeno“

        await Assert.ThrowsAsync<DomainException>(() => Categories(async s => { await s.DeleteAsync(UncategorizedId); return true; }));
        await Assert.ThrowsAsync<DomainException>(() => Categories(s => s.CreateAsync(new CategoryRequest { Name = "nezařazeno" })));
    }

    /// <summary>Po nasazení kroku 2: kopie má produkty bez dostupnosti (migrace „Unknown“) → obnova je doplní.</summary>
    [Fact]
    public async Task NeuplnaKopieKatalogu_PoStartuSeDoplniZeSkladu()
    {
        await AddProductAsync(price: 120m, stock: 50);
        await OrderingDb(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE CatalogProducts SET Availability = 'Unknown', CategoryId = NULL, CategoryName = NULL"));

        Assert.True(await CatalogSync(s => s.RequestIfEmptyOrIncompleteAsync()));
        await ProcessOutboxAsync();

        var item = Assert.Single(await Orders(s => s.GetCatalogAsync()));
        Assert.Equal("InStock", item.Availability);
        Assert.Equal("Nezařazeno", item.CategoryName);
        Assert.False(await CatalogSync(s => s.RequestIfEmptyOrIncompleteAsync()));
    }

    [Fact]
    public async Task Vyloha_HledaBezDiakritikyAVelikostiPismen_FiltrujeARadi()
    {
        var audio = await Categories(s => s.CreateAsync(new CategoryRequest { Name = "Audio" }));
        await AddProductAsync("Bezdrátová myš", 890m, stock: 40, UncategorizedId);
        await AddProductAsync("Sluchátka", 1590m, stock: 0, audio.Id);
        await AddProductAsync("Myš pro hráče", 1290m, stock: 3, UncategorizedId);

        var mys = await Shop(new CatalogSearchRequest(Query: "MYS"));
        Assert.Equal(["Bezdrátová myš", "Myš pro hráče"], mys.Items.Select(i => i.Name));

        var byCategory = await Shop(new CatalogSearchRequest(Query: "audio"));   // hledá i v názvu kategorie
        Assert.Equal("Sluchátka", Assert.Single(byCategory.Items).Name);

        var inStock = await Shop(new CatalogSearchRequest(InStockOnly: true, Sort: CatalogSort.PriceDescending));
        Assert.Equal(["Myš pro hráče", "Bezdrátová myš"], inStock.Items.Select(i => i.Name));   // vyprodaná sluchátka ne

        var audioOnly = await Shop(new CatalogSearchRequest(CategoryId: audio.Id));
        Assert.Equal("Sluchátka", Assert.Single(audioOnly.Items).Name);
        Assert.Equal([("Audio", 1), ("Nezařazeno", 2)], audioOnly.Categories.Select(c => (c.Name, c.ProductCount)));
    }

    [Fact]
    public async Task Vyloha_Strankuje()
    {
        for (var i = 1; i <= CatalogQueryService.PageSize + 2; i++)
        {
            await AddProductAsync($"Produkt {i:00}", 100m, stock: 10, UncategorizedId);
        }

        var second = await Shop(new CatalogSearchRequest(Page: 2));

        Assert.Equal(CatalogQueryService.PageSize + 2, second.TotalCount);
        Assert.Equal(2, second.PageCount);
        Assert.Equal(["Produkt 13", "Produkt 14"], second.Items.Select(i => i.Name));
        Assert.Equal(2, (await Shop(new CatalogSearchRequest(Page: 99))).Page);   // mimo rozsah = poslední stránka
    }

    // ---------- E-shop krok 2B: obrázky produktů ----------

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] OtherPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9];

    [Fact]
    public async Task Obrazek_SouborDoUloziste_JmenoPresV2DoVylohy()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);

        var updated = await Images(s => s.SetImageAsync(product.Id, Png));
        await ProcessOutboxAsync();

        Assert.Matches($@"^{product.Id}-[0-9a-f]{{12}}\.png$", updated.ImageFileName);
        Assert.Equal(Png, _images.Files[updated.ImageFileName!]);
        // Ve zprávě jen jméno souboru – ne obrázek.
        Assert.Equal(updated.ImageFileName, Assert.Single(_bus.Published.OfType<ProductUpdatedV2>()).ImageFileName);
        Assert.Equal(updated.ImageFileName, (await Orders(s => s.GetCatalogAsync())).Single().ImageFileName);
    }

    [Fact]
    public async Task NovyObrazek_MaNoveJmeno_StarySouborZmizi()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        var first = (await Images(s => s.SetImageAsync(product.Id, Png))).ImageFileName!;

        var second = (await Images(s => s.SetImageAsync(product.Id, OtherPng))).ImageFileName!;

        Assert.NotEqual(first, second);   // neměnné jméno → prohlížeč nikdy neukáže starý obrázek z cache
        Assert.Equal([second], _images.Files.Keys);
    }

    [Fact]
    public async Task StejnyObrazekZnovu_NicSeNezmeni()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Images(s => s.SetImageAsync(product.Id, Png));
        await ProcessOutboxAsync();
        _bus.Published.Clear();

        await Images(s => s.SetImageAsync(product.Id, Png));
        await ProcessOutboxAsync();

        Assert.Empty(_bus.Published);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]   // SVG = možný skript
    [InlineData("GIF89a")]
    [InlineData("obyčejný text.png")]
    public async Task NepovolenyFormat_DomainException_NicSeNeulozi(string content)
    {
        var product = await AddProductAsync(price: 100m, stock: 10);

        await Assert.ThrowsAsync<DomainException>(() =>
            Images(s => s.SetImageAsync(product.Id, System.Text.Encoding.UTF8.GetBytes(content))));

        Assert.Empty(_images.Files);
    }

    [Fact]
    public async Task PrilisVelkyObrazek_DomainException()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        var huge = new byte[ProductImageFormat.MaxBytes + 1];
        Png.CopyTo(huge, 0);

        await Assert.ThrowsAsync<DomainException>(() => Images(s => s.SetImageAsync(product.Id, huge)));
    }

    [Fact]
    public async Task OdebraniObrazku_AJehoSmazaniSProduktem_UkliziSoubory()
    {
        var first = await AddProductAsync(price: 100m, stock: 10);
        var second = await AddProductAsync(price: 200m, stock: 10);
        await Images(s => s.SetImageAsync(first.Id, Png));
        await Images(s => s.SetImageAsync(second.Id, OtherPng));

        var removed = await Images(s => s.RemoveImageAsync(first.Id));
        await Products(async s => { await s.DeleteAsync(second.Id); return true; });
        await ProcessOutboxAsync();

        Assert.Null(removed.ImageFileName);
        Assert.Empty(_images.Files);
        Assert.Null((await Orders(s => s.GetCatalogAsync())).Single().ImageFileName);
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("1-0123456789ab.svg")]
    [InlineData("C:\\Windows\\win.ini")]
    public async Task CteniObrazku_NeplatneJmeno_Null(string fileName)
    {
        Assert.Null(await Images(s => s.GetImageAsync(fileName)));
    }

    // ---------- Obnova kopie katalogu (SyncCatalogV1) ----------

    [Fact]
    public async Task ZtracenaKopieKatalogu_ObnoviSeZeSkladu()
    {
        var product = await AddProductAsync(price: 120m, stock: 5);

        // Simulace smazané ordering.db: sklad produkt má, kopie katalogu je prázdná a sama se nedoplní.
        await OrderingDb(db => db.CatalogProducts.ExecuteDeleteAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1)))));

        Assert.True(await CatalogSync(s => s.RequestIfEmptyOrIncompleteAsync()));
        await ProcessOutboxAsync();   // SyncCatalogV1 → sklad → ProductUpdatedV1 + V2 → projekce

        var catalog = await Orders(s => s.GetCatalogAsync());
        Assert.Equal(new CatalogItemDto(product.Id, "Testovací produkt", null, 120m, UncategorizedId, "Nezařazeno", "LowStock"),
            Assert.Single(catalog));
        Assert.Equal(120m, (await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1))))).TotalPrice);
    }

    [Fact]
    public async Task PlnaKopieKatalogu_PoStartuSeSkladuNeptaSe()
    {
        await AddProductAsync(price: 120m, stock: 5);
        _bus.Published.Clear();

        Assert.False(await CatalogSync(s => s.RequestIfEmptyOrIncompleteAsync()));
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
        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 2))));
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

        // Trasu jde najít podle čísla objednávky: tag má požadavek i kroky ságy v obou modulech.
        Assert.Equal(order.Id, request.GetTagItem(TraceTags.OrderId));
        var tagged = recorder.SpansOf(request.TraceId)
            .Where(a => Equals(a.GetTagItem(TraceTags.OrderId), order.Id))
            .Select(a => a.DisplayName)
            .ToList();
        Assert.Contains("dispatch OrderPlaced", tagged);
        Assert.Contains("process ReserveStockV1", tagged);
        Assert.Contains("process StockReservedV1", tagged);

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

        await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 1))));

        var placed = await OrderingDb(db => db.OutboxMessages.SingleAsync(m => m.Type == nameof(OrderPlaced)));
        Assert.Null(placed.TraceParent);
    }

    // ---------- E-shop krok 3: košík ----------

    [Fact]
    public async Task Kosik_PridaniDvakrat_JedenRadekAPrezijeDalsiPozadavek()
    {
        var mouse = await AddProductAsync("Myš", 500m, stock: 10, UncategorizedId);
        var cable = await AddProductAsync("Kabel", 100m, stock: 10, UncategorizedId);

        await Cart(s => s.AddItemAsync(Jan.CustomerId, mouse.Id, 1));
        await Cart(s => s.AddItemAsync(Jan.CustomerId, cable.Id, 3));
        var name = await Cart(s => s.AddItemAsync(Jan.CustomerId, mouse.Id, 1));

        // Každé volání běží ve vlastním scope (jako HTTP požadavek) – košík je v DB, ne v paměti.
        var cart = await Cart(s => s.GetAsync(Jan.CustomerId));
        Assert.Equal("Myš", name);
        Assert.Equal(new[] { ("Myš", 2), ("Kabel", 3) }, cart.Lines.Select(l => (l.Name, l.Quantity)));
        Assert.Equal(1300m, cart.Total);
        Assert.Equal(5, await Cart(s => s.GetTotalQuantityAsync(Jan.CustomerId)));
        Assert.Empty(_bus.Published);   // košík nic neposílá – zboží neblokuje
    }

    [Fact]
    public async Task Kosik_KazdyZakaznikMaSvuj()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);

        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 2));

        Assert.True((await Cart(s => s.GetAsync(Petra.CustomerId))).IsEmpty);
        Assert.Equal(0, await Cart(s => s.GetTotalQuantityAsync(Petra.CustomerId)));
    }

    [Fact]
    public async Task Kosik_ZmenaCenyVeSkladu_UkazeAktualniCenuAUpozorni()
    {
        var product = await AddProductAsync(price: 200m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 2));

        await Products(s => s.UpdateAsync(product.Id,
            new UpdateProductRequest { Name = "Testovací produkt", Price = 150m, CategoryId = UncategorizedId }));
        await ProcessOutboxAsync();

        var line = Assert.Single((await Cart(s => s.GetAsync(Jan.CustomerId))).Lines);
        Assert.Equal((150m, 200m, true), (line.UnitPrice, line.PriceWhenAdded, line.PriceChanged));
        Assert.Equal(300m, line.LineTotal);
    }

    [Fact]
    public async Task Kosik_Vyprodano_RadekZustaneAleNepocitaSeAPridatNejde()
    {
        var product = await AddProductAsync(price: 100m, stock: 2);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));

        await PlaceConfirmedOrderAsync(product.Id, 2, Petra);   // Petra vykoupí sklad → StockAvailabilityChangedV1

        var cart = await Cart(s => s.GetAsync(Jan.CustomerId));
        var line = Assert.Single(cart.Lines);
        Assert.Equal("OutOfStock", line.Availability);
        Assert.False(line.IsOrderable);
        Assert.Equal(0m, cart.Total);
        await Assert.ThrowsAsync<DomainException>(() => Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1)));
    }

    [Fact]
    public async Task Kosik_ProduktStazenyZNabidky_RadekJdeJenOdebrat()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));

        await Products(async s => { await s.DeleteAsync(product.Id); return true; });
        await ProcessOutboxAsync();   // ProductRemovedV1 → kopie katalogu

        var cart = await Cart(s => s.GetAsync(Jan.CustomerId));
        var line = Assert.Single(cart.Lines);
        Assert.Equal(("Testovací produkt", false, false), (line.Name, line.IsInOffer, line.IsOrderable));
        Assert.Equal(0m, cart.Total);

        await Cart(async s => { await s.RemoveItemAsync(Jan.CustomerId, product.Id); return true; });
        Assert.True((await Cart(s => s.GetAsync(Jan.CustomerId))).IsEmpty);
    }

    [Fact]
    public async Task Kosik_NeznamyProdukt_Chyba()
    {
        await Assert.ThrowsAsync<DomainException>(() => Cart(s => s.AddItemAsync(Jan.CustomerId, 999, 1)));

        Assert.Equal(0, await OrderingDb(db => db.Carts.CountAsync()));   // prázdný košík se ani nezaložil
    }

    [Fact]
    public async Task Kosik_ZmenaMnozstvi_AOdebrani()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));

        await Cart(async s => { await s.ChangeQuantityAsync(Jan.CustomerId, product.Id, 7); return true; });
        Assert.Equal(7, await Cart(s => s.GetTotalQuantityAsync(Jan.CustomerId)));
        await Assert.ThrowsAsync<DomainException>(() => Cart(async s => { await s.ChangeQuantityAsync(Jan.CustomerId, product.Id, 101); return true; }));

        await Cart(async s => { await s.RemoveItemAsync(Jan.CustomerId, product.Id); return true; });
        Assert.Equal(0, await Cart(s => s.GetTotalQuantityAsync(Jan.CustomerId)));
    }

    [Fact]
    public async Task SmazaniUctu_SmazeKosikZakaznika()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        await Cart(s => s.AddItemAsync(Petra.CustomerId, product.Id, 1));

        await Orders(s => s.AnonymizeCustomerAsync(Jan.CustomerId));

        Assert.Equal(new[] { Petra.CustomerId }, await OrderingDb(db => db.Carts.Select(c => c.CustomerId).ToListAsync()));
        Assert.Equal(1, await OrderingDb(db => db.Set<MiniShop.Domain.Orders.Carts.CartLine>().CountAsync()));   // řádky kaskádou
    }

    [Fact]
    public async Task SmazaniUctu_SNevyrizenouObjednavkou_KosikZustane()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        await PlaceConfirmedOrderAsync(product.Id, 1);

        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.AnonymizeCustomerAsync(Jan.CustomerId)));

        Assert.False((await Cart(s => s.GetAsync(Jan.CustomerId))).IsEmpty);   // všechno, nebo nic
    }

    // ---------- E-shop krok 4: pokladna ----------

    [Fact]
    public async Task Pokladna_TriProdukty_JednaObjednavkaProjdeSagouAKosikJePrazdny()
    {
        var mouse = await AddProductAsync("Myš", 200m, stock: 10, UncategorizedId);
        var cable = await AddProductAsync("Kabel", 50m, stock: 10, UncategorizedId);
        var pad = await AddProductAsync("Podložka", 100m, stock: 10, UncategorizedId);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, mouse.Id, 1));
        await Cart(s => s.AddItemAsync(Jan.CustomerId, cable.Id, 2));
        await Cart(s => s.AddItemAsync(Jan.CustomerId, pad.Id, 3));
        var checkout = await Checkout(s => s.GetAsync(Jan.CustomerId));

        var order = await Checkout(s => s.PlaceOrderAsync(Courier(checkout.CheckoutId), Jan));
        await ProcessOutboxAsync();

        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        Assert.Equal(3, order.Items.Count);
        Assert.Equal((99m, 699m, "Dlouhá 5, 110 00 Praha"), (order.ShippingPrice, order.TotalPrice, order.DeliveryAddress));
        Assert.True((await Cart(s => s.GetAsync(Jan.CustomerId))).IsEmpty);
        Assert.Equal((9, 8, 7), (await StockAsync(mouse.Id), await StockAsync(cable.Id), await StockAsync(pad.Id)));
        // OrderPlacedV1 se nezměnil – TotalPrice je celková cena včetně dopravy.
        Assert.Equal(699m, Assert.Single(_bus.Published.OfType<OrderPlacedV1>()).TotalPrice);
    }

    [Fact]
    public async Task Pokladna_DvojiOdeslani_JednaObjednavka()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        var checkoutId = (await Checkout(s => s.GetAsync(Jan.CustomerId))).CheckoutId;

        var first = await Checkout(s => s.PlaceOrderAsync(Pickup(checkoutId), Jan));
        var second = await Checkout(s => s.PlaceOrderAsync(Pickup(checkoutId), Jan));   // F5 / dvojklik

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await OrderingDb(db => db.Orders.CountAsync()));
    }

    [Fact]
    public async Task Pokladna_SoubezneOdeslani_JednaObjednavka()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        var checkoutId = (await Checkout(s => s.GetAsync(Jan.CustomerId))).CheckoutId;

        // Dva požadavky naráz (každý ve vlastním scope a DbContextu) – druhý narazí na verzi košíku nebo na unikátní index.
        var results = await Task.WhenAll(
            Checkout(s => s.PlaceOrderAsync(Pickup(checkoutId), Jan)),
            Checkout(s => s.PlaceOrderAsync(Pickup(checkoutId), Jan)));

        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(1, await OrderingDb(db => db.Orders.CountAsync()));
    }

    [Fact]
    public async Task Pokladna_ZmenaCeny_ChybaAzDoPotvrzeni()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 2));
        await Products(s => s.UpdateAsync(product.Id,
            new UpdateProductRequest { Name = "Testovací produkt", Price = 130m, CategoryId = UncategorizedId }));
        await ProcessOutboxAsync();
        var checkout = await Checkout(s => s.GetAsync(Jan.CustomerId));
        Assert.True(checkout.HasPriceChanges);
        Assert.False(checkout.CanPlaceOrder);

        await Assert.ThrowsAsync<DomainException>(() => Checkout(s => s.PlaceOrderAsync(Pickup(checkout.CheckoutId), Jan)));
        Assert.Equal(0, await OrderingDb(db => db.Orders.CountAsync()));   // žádné tiché zdražení

        await Checkout(async s => { await s.AcceptCurrentPricesAsync(Jan.CustomerId); return true; });
        var accepted = await Checkout(s => s.GetAsync(Jan.CustomerId));
        var order = await Checkout(s => s.PlaceOrderAsync(Pickup(accepted.CheckoutId), Jan));

        Assert.Equal(260m, order.TotalPrice);
    }

    [Fact]
    public async Task Pokladna_StaryFormularPoZmeneKosiku_Chyba()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        var seen = (await Checkout(s => s.GetAsync(Jan.CustomerId))).CheckoutId;

        await Cart(async s => { await s.ChangeQuantityAsync(Jan.CustomerId, product.Id, 5); return true; });   // jiná záložka

        await Assert.ThrowsAsync<DomainException>(() => Checkout(s => s.PlaceOrderAsync(Pickup(seen), Jan)));
        Assert.Equal(5, await Cart(s => s.GetTotalQuantityAsync(Jan.CustomerId)));
    }

    [Fact]
    public async Task Pokladna_NedostatekJednohoProduktu_ZamitnutaCelaANicSeNezarezervuje()
    {
        var plenty = await AddProductAsync("Kabel", 50m, stock: 10, UncategorizedId);
        var scarce = await AddProductAsync("Myš", 200m, stock: 1, UncategorizedId);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, plenty.Id, 2));
        await Cart(s => s.AddItemAsync(Jan.CustomerId, scarce.Id, 2));
        var checkoutId = (await Checkout(s => s.GetAsync(Jan.CustomerId))).CheckoutId;

        var order = await Checkout(s => s.PlaceOrderAsync(Pickup(checkoutId), Jan));
        await ProcessOutboxAsync();

        var rejected = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("Myš", rejected.RejectionReason);
        Assert.Equal((10, 1), (await StockAsync(plenty.Id), await StockAsync(scarce.Id)));   // všechno, nebo nic
    }

    [Fact]
    public async Task Pokladna_KuryrNadLimit_Zdarma_KuryrBezAdresy_Chyba()
    {
        var product = await AddProductAsync(price: 600m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 2));
        var checkout = await Checkout(s => s.GetAsync(Jan.CustomerId));
        Assert.Equal(0m, checkout.ShippingOptions.Single(o => o.Method == ShippingMethod.Courier).Price);

        var noAddress = new CheckoutRequest { CheckoutId = checkout.CheckoutId, ShippingMethod = ShippingMethod.Courier };
        await Assert.ThrowsAsync<DomainException>(() => Checkout(s => s.PlaceOrderAsync(noAddress, Jan)));
        Assert.False((await Cart(s => s.GetAsync(Jan.CustomerId))).IsEmpty);   // košík zůstal

        var order = await Checkout(s => s.PlaceOrderAsync(Courier(checkout.CheckoutId), Jan));
        Assert.Equal((0m, 1200m), (order.ShippingPrice, order.TotalPrice));
    }

    [Fact]
    public async Task Pokladna_PredvyplniAdresuZPosledniObjednavky_SmazaniUctuJiSmaze()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        var order = await Checkout(async s => await s.PlaceOrderAsync(Courier((await s.GetAsync(Jan.CustomerId)).CheckoutId), Jan));
        await ProcessOutboxAsync();
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));

        Assert.Equal(new DeliveryAddressDto("Dlouhá 5", "Praha", "11000"), (await Checkout(s => s.GetAsync(Jan.CustomerId))).LastAddress);

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await Orders(s => s.AnonymizeCustomerAsync(Jan.CustomerId));
        Assert.Null(await OrderingDb(db => db.Orders.AsNoTracking().Select(o => o.DeliveryAddress).SingleAsync()));
    }

    [Fact]
    public async Task Api_StejneCheckoutId_JednaObjednavka()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        var request = CreateRequest((product.Id, 1));
        request.CheckoutId = Guid.NewGuid();

        var first = await Orders(s => PlaceAsJan(s, request));
        var retry = await Orders(s => PlaceAsJan(s, request));   // klient po výpadku sítě zopakuje požadavek

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal("None", first.ShippingMethod);
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

    private async Task<OrderDto> PlaceConfirmedOrderAsync(int productId, int quantity, OrderCustomer? customer = null)
    {
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((productId, quantity)), customer ?? Jan));
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

    private async Task<string> StatusAsync(int orderId) => (await Orders(s => s.GetByIdAsync(orderId, OrderAccess.AllOrders))).Status;

    private Task<OrderSaga> SagaAsync(int orderId) => OrderingDb(db => db.OrderSagas.SingleAsync(s => s.OrderId == orderId));

    private Task<int> StockAsync() => InventoryDb(async db => (await db.Products.SingleAsync()).StockQuantity);

    private Task<int> StockAsync(int productId) =>
        InventoryDb(async db => (await db.Products.SingleAsync(p => p.Id == productId)).StockQuantity);

    /// <summary>
    /// Založí produkt ve skladu a nechá ho promítnout do kopie katalogu v Orderingu (krok 10) –
    /// teprve pak jde objednat. Zprávy z přípravy dat se z "brokeru" smažou, ať nepletou testy.
    /// </summary>
    private async Task<ProductDto> AddProductAsync(string name, decimal price, int stock, int categoryId)
    {
        var product = await Products(s => s.CreateAsync(new CreateProductRequest
        {
            Name = name,
            Price = price,
            StockQuantity = stock,
            CategoryId = categoryId
        }));
        await ProcessOutboxAsync();
        _bus.Published.Clear();
        return product;
    }

    /// <summary>E-shop krok 2B: obrázky produktů (modul Inventory) ve vlastním scope.</summary>
    private async Task<T> Images<T>(Func<IProductImageService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IProductImageService>());
    }

    /// <summary>E-shop krok 2: správa kategorií (modul Inventory) ve vlastním scope.</summary>
    private async Task<T> Categories<T>(Func<ICategoryService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICategoryService>());
    }

    /// <summary>E-shop krok 4: pokladna ve vlastním scope.</summary>
    private async Task<T> Checkout<T>(Func<ICheckoutService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICheckoutService>());
    }

    private static CheckoutRequest Pickup(Guid checkoutId) =>
        new() { CheckoutId = checkoutId, ShippingMethod = ShippingMethod.Pickup };

    private static CheckoutRequest Courier(Guid checkoutId) => new()
    {
        CheckoutId = checkoutId,
        ShippingMethod = ShippingMethod.Courier,
        Street = "Dlouhá 5",
        City = "Praha",
        PostalCode = "110 00"
    };

    /// <summary>E-shop krok 3: košík ve vlastním scope.</summary>
    private async Task<T> Cart<T>(Func<ICartService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICartService>());
    }

    /// <summary>E-shop krok 2: výloha (čtení kopie katalogu) ve vlastním scope.</summary>
    private async Task<CatalogSearchResult> Shop(CatalogSearchRequest request)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICatalogQueryService>().SearchAsync(request);
    }

    private async Task<ProductDto> AddProductAsync(decimal price, int stock)
    {
        var product = await Products(s => s.CreateAsync(new CreateProductRequest
        {
            Name = "Testovací produkt",
            Price = price,
            StockQuantity = stock,
            CategoryId = UncategorizedId
        }));
        await ProcessOutboxAsync();
        _bus.Published.Clear();
        return product;
    }

    private static CreateOrderRequest CreateRequest(params (int ProductId, int Quantity)[] items) => new()
    {
        Items = items.Select(i => new CreateOrderItemRequest { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
    };

    // Účty krok 3: kdo objednává – přihlášený zákazník (Web ho bere z cookie).
    private static readonly OrderCustomer Jan = new("zakaznik-jan", "Jan Novák", "jan@example.com");
    private static readonly OrderCustomer Petra = new("zakaznik-petra", "Petra Malá", "petra@example.com");

    private static Task<OrderDto> PlaceAsJan(IOrderService service, CreateOrderRequest request) =>
        service.PlaceOrderAsync(request, Jan);

    public void Dispose()
    {
        _services.Dispose();
        _orderingDatabase.Dispose();
        _inventoryDatabase.Dispose();
    }
}
