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
using MiniShop.Application.Ordering.Coupons;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Ordering.Checkout;
using MiniShop.Application.Notifications;
using MiniShop.Application.Payments;
using MiniShop.Application.Reporting;
using MiniShop.Application.Reviews;
using MiniShop.Application.Ordering.IntegrationEvents;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Contracts;
using MiniShop.Contracts.Carts;
using MiniShop.Contracts.Customers;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Contracts.Reporting;
using MiniShop.Contracts.Reviews;
using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Coupons;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Domain.Payments;
using MiniShop.Domain.Reporting;
using MiniShop.Domain.Reviews;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Payments;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Infrastructure.Reviews;
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
    private static readonly TimeSpan PaymentTimeout = TimeSpan.FromMinutes(30);   // e-shop krok 8
    private static readonly TimeSpan CartAbandonedAfter = TimeSpan.FromHours(24);   // e-shop krok 10
    private static readonly TimeSpan ReturnWindow = TimeSpan.FromDays(14);           // e-shop krok 12
    private const string WebhookSecret = "testovaci-tajemstvi-webhooku-plateb-0123456789";

    private readonly TestDatabases _databases;   // škálování krok 2: SQLite (databáze na modul) nebo PostgreSQL (schéma na modul)
    private readonly FixedTimeProvider _time = new(Now);
    private readonly RecordingEmailSender _emails = new();
    private readonly InMemoryCustomerContacts _contacts = new();   // e-shop krok 10: účty se souhlasem
    private readonly InMemoryProductImageStore _images = new();   // e-shop krok 2B
    private readonly List<IDomainEvent> _dispatched = new();
    private readonly ServiceProvider _services;
    private readonly RecordingIntegrationBus _bus;

    public OrderServiceTests() : this(new TestDatabases())
    {
    }

    /// <summary>Škálování krok 3: scénáře nad zadanými databázemi – test převodu je pouští nad SQLite a výsledek převádí.</summary>
    internal OrderServiceTests(TestDatabases databases)
    {
        _databases = databases;
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(_time);
        services.AddInfrastructure(_databases.Ordering, _databases.Inventory);

        // Stejné služby a handlery jako v aplikaci (ApplicationServiceCollectionExtensions).
        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton<IDiscountPolicy>(new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m));
        services.AddDomainEventHandler<OrderConfirmed, PublishOrderIntegrationEventsHandler>();   // e-shop krok 7
        services.AddDomainEventHandler<OrderRejected, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderPlaced, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderShipped, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCompleted, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCancelled, PublishOrderIntegrationEventsHandler>();

        // Sága (orchestrátor), kopie katalogu (krok 10) a modul Inventory.
        services.AddSingleton(new OrderSagaSettings(ReservationTimeout, PaymentTimeout));
        services.AddDomainEventHandler<OrderPlaced, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderShipped, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCompleted, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCancelled, OrderSagaOrchestrator>();
        services.AddIntegrationMessageHandler<StockReservedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReservationFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReleasedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<PaymentSucceededV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);   // e-shop krok 8
        services.AddIntegrationMessageHandler<PaymentFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);

        // E-shop krok 12: vrácení zboží (sága vratky, překlad událostí vratky) – jako AddOrderingModule ve Webu.
        services.AddSingleton(new ReturnSettings(ReturnWindow, ReturnWindow));
        services.AddScoped<ReturnSagaOrchestrator>();
        services.AddDomainEventHandler<OrderReturnRequested, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderReturnRejected, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderReturned, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderReturnRequested, ReturnSagaOrchestrator>();
        services.AddIntegrationMessageHandler<ReturnedGoodsRestockedV1, ReturnSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<PaymentRefundedV1, ReturnSagaOrchestrator>(IntegrationModules.Ordering);
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
        services.AddScoped<CouponService>();                                       // e-shop krok 13
        services.AddDomainEventHandler<OrderRejected, ReleaseCouponHandler>();
        services.AddDomainEventHandler<OrderCancelled, ReleaseCouponHandler>();
        services.AddSingleton<IShippingPriceCalculator>(new StandardShippingPriceCalculator(courierPrice: 99m, freeFrom: 1_000m));
        services.AddIntegrationMessageHandler<ReserveStockV1, ReserveStockHandler>(IntegrationModules.Inventory);
        services.AddIntegrationMessageHandler<ReleaseStockV1, ReleaseStockHandler>(IntegrationModules.Inventory);
        services.AddIntegrationMessageHandler<RestockReturnedGoodsV1, RestockReturnedGoodsHandler>(IntegrationModules.Inventory);   // e-shop krok 12
        services.AddIntegrationMessageHandler<OrderShippedV1, FulfilReservationHandler>(IntegrationModules.Inventory);
        services.AddIntegrationMessageHandler<SyncCatalogV1, SyncCatalogHandler>(IntegrationModules.Inventory);
        services.AddScoped<CatalogSyncService>();

        // E-shop krok 7: modul Notifications (e-maily podle integračních událostí) – jako AddNotificationsModule ve Webu.
        services.AddNotificationsInfrastructure(_databases.Notifications);
        services.AddSingleton(new NotificationSettings("https://minishop.test"));
        services.AddSingleton<OrderEmailTemplates>();
        services.AddIntegrationMessageHandler<OrderPlacedV2, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderConfirmedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderRejectedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderShippedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderCompletedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderCancelledV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<CustomerAnonymizedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<PaymentRefundedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnRequestedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);   // e-shop krok 12
        services.AddIntegrationMessageHandler<ReturnRejectedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);

        // E-shop krok 10: opuštěný košík – služba v Orderingu, připomínka v Notifications, kontakty místo účtů v paměti.
        services.AddSingleton(new CartReminderSettings(CartAbandonedAfter, TimeSpan.FromMinutes(15)));
        services.AddScoped<CartReminderService>();
        services.AddSingleton<CartEmailTemplates>();
        services.AddSingleton<ICustomerContacts>(_contacts);
        services.AddIntegrationMessageHandler<CartAbandonedV1, CartReminderHandler>(IntegrationModules.Notifications);

        // E-shop krok 8: modul Payments (účastník ságy) – jako AddPaymentsModule + AddPaymentsInfrastructure ve Webu.
        services.AddPaymentsInfrastructure(_databases.Payments, WebhookSecret);
        services.AddIntegrationMessageHandler<RequestPaymentV1, PaymentCommandHandlers>(IntegrationModules.Payments);
        services.AddIntegrationMessageHandler<RefundPaymentV1, PaymentCommandHandlers>(IntegrationModules.Payments);

        // E-shop krok 11: modul Reporting (přehled tržeb) – jako AddReportingModule + AddReportingInfrastructure ve Webu,
        // a snímky objednávek pro přestavění (handler v Orderingu).
        services.AddReportingInfrastructure(_databases.Reporting);
        services.AddScoped<SalesReportService>();
        services.AddIntegrationMessageHandler<OrderPlacedV2, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderConfirmedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderRejectedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderCancelledV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderCompletedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<PaymentRefundedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderSnapshotV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, SalesProjectionHandler>(IntegrationModules.Reporting);   // e-shop krok 12
        services.AddIntegrationMessageHandler<RebuildSalesReportV1, RebuildSalesReportHandler>(IntegrationModules.Ordering);

        // E-shop krok 14: modul Reviews (kopie nákupů, hodnocení) – jako AddReviewsModule + AddReviewsInfrastructure ve Webu,
        // a v Orderingu snímky nákupů a souhrn hodnocení do kopie katalogu.
        services.AddReviewsInfrastructure(_databases.Reviews);
        services.AddScoped<ReviewService>();
        services.AddIntegrationMessageHandler<OrderPlacedV2, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<OrderCompletedV1, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<OrderSnapshotV2, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<CustomerAnonymizedV1, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<SyncPurchasesV1, SyncPurchasesHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductRatingChangedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);

        // Testovací náhrady: notifikace do seznamu, "broker" si zprávy zapamatuje a doručí je v procesu,
        // skutečný dispatcher obalený záznamníkem.
        services.AddSingleton<IEmailSender>(_emails);
        services.AddSingleton<IIntegrationBus>(sp =>
            new RecordingIntegrationBus(ActivatorUtilities.CreateInstance<InProcessIntegrationBus>(sp)));
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<IDomainEventDispatcher>(sp =>
            new RecordingDomainEventDispatcher(_dispatched, sp.GetRequiredService<DomainEventDispatcher>()));

        _services = services.BuildServiceProvider();
        _bus = (RecordingIntegrationBus)_services.GetRequiredService<IIntegrationBus>();
        OrderingDatabaseInitializer.InitializeAsync(_services).GetAwaiter().GetResult();
        NotificationsDatabaseInitializer.InitializeAsync(_services).GetAwaiter().GetResult();
        PaymentsDatabaseInitializer.InitializeAsync(_services).GetAwaiter().GetResult();
        ReportingDatabaseInitializer.InitializeAsync(_services).GetAwaiter().GetResult();
        ReviewsDatabaseInitializer.InitializeAsync(_services).GetAwaiter().GetResult();
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
        Assert.Empty(NotifierEvents);
    }

    [Fact]
    public async Task PlaceOrder_SkladZarezervuje_ObjednavkaPotvrzena()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        Assert.Equal(2, await StockAsync());
        Assert.Contains($"confirmed:{order.Id}", NotifierEvents);

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
        Assert.Contains($"rejected:{order.Id}", NotifierEvents);
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
        Assert.Contains($"cancelled:{order.Id}", NotifierEvents);
        Assert.Single(_bus.Published.OfType<ReleaseStockV1>());
        Assert.Single(_bus.Published.OfType<StockReleasedV1>());
        Assert.Equal(OrderSagaState.Released, (await SagaAsync(order.Id)).State);
        Assert.All(await InventoryDb(db => db.StockReservations.ToListAsync()), r => Assert.NotNull(r.ReleasedAtUtc));
    }


    [Fact]
    public async Task Cancel_OdeslanouObjednavku_VyhodiDomainException()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await ShipAsync(order.Id);

        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders)));
    }

    // ---------- E-shop krok 6: expedice ----------

    [Fact]
    public async Task Expedice_BaleniOdeslaniDoruceni_ZboziZustaneProdaneARezervaceVydana()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        // Dřív „Vyřídit“ rovnou z Confirmed – teď to nejde.
        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CompleteAsync(order.Id)));

        await ShipAsync(order.Id);
        await ProcessOutboxAsync();   // OrderShippedV1 → sklad vydá rezervaci, sága → Shipped

        var shipped = Assert.Single(_bus.Published.OfType<OrderShippedV1>());
        Assert.Equal((order.Id, "PPL", "123456"), (shipped.OrderId, shipped.Carrier, shipped.TrackingNumber));
        Assert.Contains($"shipped:{order.Id}", NotifierEvents);
        Assert.Equal(OrderSagaState.Shipped, (await SagaAsync(order.Id)).State);
        var reservation = Assert.Single(await InventoryDb(db => db.StockReservations.Where(r => r.OrderId == order.Id).ToListAsync()));
        Assert.NotNull(reservation.FulfilledAtUtc);
        Assert.Null(reservation.ReleasedAtUtc);

        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();

        Assert.Contains($"completed:{order.Id}", NotifierEvents);
        Assert.Equal(4, await StockAsync());   // výdej zásobu nemění – odečetla se při rezervaci
        Assert.Equal(OrderSagaState.Completed, (await SagaAsync(order.Id)).State);
        var detail = await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal(["Pending", "Confirmed", "Packing", "Shipped", "Completed"], detail.History.Select(h => h.Status));
    }

    [Fact]
    public async Task Expedice_ZruseniBehemBaleni_ZboziSeVratiNaSklad()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 2);
        await Orders(s => s.StartPackingAsync(order.Id));

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await ProcessOutboxAsync();

        Assert.Equal(5, await StockAsync());
        Assert.Equal(OrderSagaState.Released, (await SagaAsync(order.Id)).State);
        Assert.Empty(_bus.Published.OfType<OrderShippedV1>());
    }

    [Fact]
    public async Task Expedice_FrontaObsahujeJenRozpracovane()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        var confirmed = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        var shipped = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        var cancelled = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await ShipAsync(shipped.Id);
        await Orders(s => s.CancelAsync(cancelled.Id, OrderAccess.AllOrders));

        var queue = await Orders(s => s.GetFulfilmentQueueAsync());

        Assert.Equal([(confirmed.Id, "Confirmed"), (shipped.Id, "Shipped")], queue.Select(o => (o.Id, o.Status)));
    }

    /// <summary>
    /// E-shop krok 6: souběh – admin a zákazník načtou stejnou objednávku, oba ji změní. Uloží se jen první změna,
    /// druhá skončí ConcurrencyConflictException (Version už nesedí) a nic nepřepíše.
    /// </summary>
    [Fact]
    public async Task Soubeh_OdeslatAZrusitZaroven_VyhrajeJedenADataSedi()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var placed = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await Orders(s => s.StartPackingAsync(placed.Id));

        await using var adminScope = _services.CreateAsyncScope();
        await using var customerScope = _services.CreateAsyncScope();
        var adminDb = adminScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var customerDb = customerScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var forAdmin = await adminDb.Orders.SingleAsync(o => o.Id == placed.Id);       // oba vidí Packing
        var forCustomer = await customerDb.Orders.SingleAsync(o => o.Id == placed.Id);

        forCustomer.Cancel(Now.UtcDateTime);
        await customerDb.SaveChangesAsync();                                           // zákazník byl rychlejší
        forAdmin.Ship("PPL", "123456", Now.UtcDateTime);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => adminDb.SaveChangesAsync());
        await ProcessOutboxAsync();

        Assert.Equal("Cancelled", await StatusAsync(placed.Id));
        Assert.Empty(_bus.Published.OfType<OrderShippedV1>());                         // odeslání se neuložilo ani do outboxu
        Assert.Equal(5, await StockAsync());                                           // zboží zpátky, nic nevydáno
        var detail = await Orders(s => s.GetDetailAsync(placed.Id, OrderAccess.AllOrders));
        Assert.Equal(["Pending", "Confirmed", "Packing", "Cancelled"], detail.History.Select(h => h.Status));
    }

    /// <summary>
    /// E-shop krok 7: e-mail o zrušení poprvé selže (SMTP nejde). Selže jen doručení OrderCancelledV1 do Notifications
    /// – zpráva se zopakuje a e-mail odejde právě jednou. Objednávky ani skladu se výpadek e-mailu netýká
    /// (dřív byl e-mail handler uvnitř Orderingu a jeho chyba opakovala i doménovou událost).
    /// </summary>
    [Fact]
    public async Task Cancel_EmailPoprveSelze_PriOpakovaniJedenEmailAZboziJednou()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 2);
        _emails.FailNext = ("je zrušená", 1);

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));
        await ProcessOutboxAsync();
        Assert.Empty(NotifierEvents.Where(e => e == $"cancelled:{order.Id}"));   // EDA krok 13: čeká na backoff

        _time.Advance(OutboxProcessor.RetryDelay(1));
        await ProcessOutboxAsync();

        Assert.Equal(5, await StockAsync());
        Assert.Single(_bus.Published.OfType<ReleaseStockV1>());
        Assert.Single(NotifierEvents, e => e == $"cancelled:{order.Id}");
        var message = await OrderingDb(db => db.OutboxMessages.SingleAsync(m => m.Type == nameof(OrderCancelledV1)));
        Assert.NotNull(message.ProcessedAtUtc);   // napodruhé doručeno
    }

    // ---------- E-shop krok 8: platba kartou ----------

    [Fact]
    public async Task Platba_Zaplaceno_ObjednavkaPotvrzena()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 2);

        await ProcessOutboxAsync();   // rezervace → RequestPaymentV1 → Payments založí platbu
        Assert.Equal("AwaitingPayment", await StatusAsync(order.Id));
        Assert.Equal(3, await StockAsync());   // zboží drží rezervace
        var request = Assert.Single(_bus.Published.OfType<RequestPaymentV1>());
        Assert.Equal((order.TotalPrice, Now.UtcDateTime + PaymentTimeout), (request.Amount, request.ExpiresAtUtc));

        Assert.Equal(PaymentWebhookResult.Accepted, await SimulateGatewayAsync(order.Id, paid: true));
        await ProcessOutboxAsync();   // PaymentSucceededV1 → sága potvrdí

        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        Assert.Equal(OrderSagaState.Reserved, (await SagaAsync(order.Id)).State);
        Assert.Equal(3, await StockAsync());
        var detail = await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal(["Pending", "AwaitingPayment", "Confirmed"], detail.History.Select(h => h.Status));
    }

    [Fact]
    public async Task Platba_Zamitnuta_ObjednavkaZamitnutaAZboziZpet()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 2);
        await ProcessOutboxAsync();

        await SimulateGatewayAsync(order.Id, paid: false);
        await ProcessOutboxAsync();   // PaymentFailedV1 → zamítnout + ReleaseStockV1

        var rejected = await Orders(s => s.GetByIdAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("zamítli", rejected.RejectionReason);
        Assert.Equal(5, await StockAsync());
        Assert.Equal(OrderSagaState.Released, (await SagaAsync(order.Id)).State);
        Assert.Contains($"rejected:{order.Id}", NotifierEvents);
    }

    [Fact]
    public async Task Platba_Nezaplaceno_PoTerminuZamitnutaAPozdniPlatbaSeVrati()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 2);
        await ProcessOutboxAsync();

        _time.Advance(PaymentTimeout);
        Assert.Equal([order.Id], await ExpiredSagasAsync());
        Assert.True(await Saga(s => s.TimeOutAsync(order.Id, CancellationToken.None)));
        await ProcessOutboxAsync();

        Assert.Equal("Rejected", await StatusAsync(order.Id));
        Assert.Equal(5, await StockAsync());

        // Brána platbu přesto potvrdí (zákazník zaplatil v poslední vteřině) → sága pošle vrátit peníze.
        await SimulateGatewayAsync(order.Id, paid: true, ignoreExpiry: true);
        await ProcessOutboxAsync();

        Assert.Single(_bus.Published.OfType<RefundPaymentV1>());
        Assert.Single(_bus.Published.OfType<PaymentRefundedV1>());
        Assert.Equal("Rejected", await StatusAsync(order.Id));   // objednávka zůstala zamítnutá
        Assert.Contains(_emails.Sent, e => e.Subject.Contains("vracíme"));
    }

    [Fact]
    public async Task Platba_Dobirka_SagaPlatbuPreskoci()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 1, PaymentMethod.CashOnDelivery);

        await ProcessOutboxAsync();

        Assert.Equal("Confirmed", await StatusAsync(order.Id));
        Assert.Empty(_bus.Published.OfType<RequestPaymentV1>());
    }

    [Fact]
    public async Task Platba_OpakovanyWebhook_NicNezdvoji()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 1);
        await ProcessOutboxAsync();

        await SimulateGatewayAsync(order.Id, paid: true);
        await SimulateGatewayAsync(order.Id, paid: true);   // brána neviděla odpověď a poslala znovu
        await ProcessOutboxAsync();

        Assert.Single(_bus.Published.OfType<PaymentSucceededV1>());
        Assert.Single(NotifierEvents, e => e == $"confirmed:{order.Id}");
    }

    // ---------- E-shop krok 9: zrušení kdykoli před odesláním ----------

    [Fact]
    public async Task Zruseni_PredOdpovediSkladu_PozdniRezervaceSeUvolni()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => PlaceAsJan(s, CreateRequest((product.Id, 2))));
        await ProcessOneRoundAsync();   // sága založena, ReserveStockV1 čeká v outboxu – sklad ještě nic neví

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await ProcessOutboxAsync();     // sklad zarezervuje → sága ví o zrušení → ReleaseStockV1

        Assert.Equal("Cancelled", await StatusAsync(order.Id));
        Assert.Single(_bus.Published.OfType<StockReservedV1>());
        Assert.Single(_bus.Published.OfType<ReleaseStockV1>());
        Assert.Equal(5, await StockAsync());
        Assert.Equal(OrderSagaState.Released, (await SagaAsync(order.Id)).State);
        Assert.Contains($"cancelled:{order.Id}", NotifierEvents);
        Assert.DoesNotContain($"confirmed:{order.Id}", NotifierEvents);
    }

    [Fact]
    public async Task Zruseni_BehemCekaniNaPlatbu_ZboziZpet_PlatbaZustaneNezaplacena()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 2);
        await ProcessOutboxAsync();
        Assert.Equal("AwaitingPayment", await StatusAsync(order.Id));

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await ProcessOutboxAsync();

        Assert.Equal("Cancelled", await StatusAsync(order.Id));
        Assert.Equal(5, await StockAsync());
        Assert.Equal(OrderSagaState.Released, (await SagaAsync(order.Id)).State);
        Assert.Empty(_bus.Published.OfType<RefundPaymentV1>());   // nic se nezaplatilo
        Assert.Equal(PaymentStatus.Pending, await PaymentStatusAsync(order.Id));

        _time.Advance(PaymentTimeout);
        Assert.Empty(await ExpiredSagasAsync());                  // termín platby u zrušené už neběží
    }

    [Fact]
    public async Task Zruseni_PlatbaPredbehlaZruseni_SagaZruseniDozeneAPenizeVrati()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 2);
        await ProcessOutboxAsync();

        // Zákazník zruší a skoro zároveň zaplatí: OrderCancelled ještě čeká v outboxu, když sága dostane platbu.
        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await SimulateGatewayAsync(order.Id, paid: true);
        await RedeliverAsync(new PaymentSucceededV1(Guid.NewGuid(), Now.UtcDateTime, order.Id, order.TotalPrice),
            IntegrationModules.Ordering);   // sága nesmí potvrdit zrušenou objednávku – dožene zrušení a peníze vrátí
        await ProcessOutboxAsync();

        Assert.Equal("Cancelled", await StatusAsync(order.Id));
        Assert.Equal(5, await StockAsync());
        Assert.Single(_bus.Published.OfType<RefundPaymentV1>());   // i když PaymentSucceededV1 přišla dvakrát
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusAsync(order.Id));
        Assert.DoesNotContain($"confirmed:{order.Id}", NotifierEvents);
        Assert.Contains(_emails.Sent, e => e.Subject.Contains("vracíme"));
    }

    [Fact]
    public async Task Zruseni_ZaplaceneObjednavky_VratiZboziIPenize()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 2);
        await ProcessOutboxAsync();
        await SimulateGatewayAsync(order.Id, paid: true);
        await ProcessOutboxAsync();
        Assert.Equal("Confirmed", await StatusAsync(order.Id));

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await ProcessOutboxAsync();

        Assert.Equal(5, await StockAsync());
        Assert.Single(_bus.Published.OfType<RefundPaymentV1>());
        Assert.Single(_bus.Published.OfType<PaymentRefundedV1>());
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusAsync(order.Id));
        Assert.Contains($"cancelled:{order.Id}", NotifierEvents);
        Assert.Contains(_emails.Sent, e => e.Subject.Contains("vracíme"));
    }

    [Fact]
    public async Task Zruseni_PotvrzeneDobirky_PenizeNevraci()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceCardOrderAsync(product.Id, quantity: 2, PaymentMethod.CashOnDelivery);
        await ProcessOutboxAsync();

        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await ProcessOutboxAsync();

        Assert.Equal(5, await StockAsync());
        Assert.Empty(_bus.Published.OfType<RefundPaymentV1>());
    }

    // ---------- E-shop krok 10: opuštěný košík ----------

    [Fact]
    public async Task OpustenyKosik_SeSouhlasem_JednaPripominkaSOdkazy()
    {
        AllowReminders(consent: true);
        var product = await AddProductAsync(price: 100m, stock: 5);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 2));

        _time.Advance(CartAbandonedAfter - TimeSpan.FromMinutes(1));
        Assert.Empty(await Reminders(s => s.GetAbandonedCartIdsAsync()));   // ještě to není „ticho“

        _time.Advance(TimeSpan.FromMinutes(1));
        await RunCartReminderWorkerAsync();
        await RunCartReminderWorkerAsync();   // další kolo workeru už nic nepošle
        await ProcessOutboxAsync();

        var abandoned = Assert.Single(_bus.Published.OfType<CartAbandonedV1>());
        Assert.Equal(Jan.CustomerId, abandoned.CustomerId);
        Assert.Equal((2, 100m), (Assert.Single(abandoned.Items).Quantity, abandoned.Items[0].UnitPrice));
        var email = Assert.Single(_emails.Sent);
        Assert.Equal("jan@example.com", email.To);
        Assert.Contains("2 × Testovací produkt", email.TextBody);
        Assert.Contains("https://minishop.test/Cart", email.TextBody);
        Assert.Contains("https://minishop.test/Account/EmailPreferences", email.HtmlBody);
    }

    [Theory]
    [InlineData(false, true)]    // bez souhlasu
    [InlineData(true, false)]    // nepotvrzený e-mail
    public async Task OpustenyKosik_BezSouhlasuNeboPotvrzeni_ZadnyEmail(bool consent, bool emailConfirmed)
    {
        AllowReminders(consent, emailConfirmed);
        var product = await AddProductAsync(price: 100m, stock: 5);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));

        _time.Advance(CartAbandonedAfter);
        await RunCartReminderWorkerAsync();
        await ProcessOutboxAsync();

        Assert.Single(_bus.Published.OfType<CartAbandonedV1>());   // fakt se oznámí, rozhoduje odběratel
        Assert.Empty(_emails.Sent);
    }

    [Fact]
    public async Task OpustenyKosik_SouhlasOdvolanyPredOdeslanim_ZadnyEmail()
    {
        AllowReminders(consent: true);
        var product = await AddProductAsync(price: 100m, stock: 5);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        _time.Advance(CartAbandonedAfter);
        await RunCartReminderWorkerAsync();   // CartAbandonedV1 čeká v outboxu

        AllowReminders(consent: false);       // zákazník mezitím připomínky vypnul
        await ProcessOutboxAsync();

        Assert.Empty(_emails.Sent);           // souhlas se ověřuje až při odeslání
    }

    [Fact]
    public async Task OpustenyKosik_ZmenaMeziVyhledanimAOznamenim_NicNeodejde()
    {
        AllowReminders(consent: true);
        var product = await AddProductAsync(price: 100m, stock: 5);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        _time.Advance(CartAbandonedAfter);

        var cartId = Assert.Single(await Reminders(s => s.GetAbandonedCartIdsAsync()));
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));   // zákazník se vrátil

        Assert.False(await Reminders(s => s.RemindAsync(cartId)));
        Assert.Empty(_bus.Published.OfType<CartAbandonedV1>());
    }

    [Fact]
    public async Task OpustenyKosik_PoObjednaniNovyNakup_DalsiPripominka()
    {
        AllowReminders(consent: true);
        var product = await AddProductAsync(price: 100m, stock: 5);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        _time.Advance(CartAbandonedAfter);
        await RunCartReminderWorkerAsync();

        await PlaceCardOrderAsync(product.Id, quantity: 1, PaymentMethod.CashOnDelivery);   // nákup dokončen → košík prázdný
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));                   // nový nákup
        _time.Advance(CartAbandonedAfter);
        await RunCartReminderWorkerAsync();
        await ProcessOutboxAsync();

        Assert.Equal(2, _bus.Published.OfType<CartAbandonedV1>().Count());
        Assert.Equal(2, _emails.Sent.Count(e => e.Subject.Contains("košíku")));
    }

    // ---------- E-shop krok 14: hodnocení produktů ----------

    [Fact]
    public async Task Hodnoceni_AzPoDoruceni_SouhrnDorazilDoVylohy()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);   // zaplaceno, ale nedoručeno

        var notYet = await Assert.ThrowsAsync<DomainException>(() => Reviews(s => s.SubmitAsync(product.Id, Jan.CustomerId, 5, "Super")));
        Assert.Contains("dostali", notYet.Message);

        await ShipAsync(order.Id);
        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();   // OrderCompletedV1 → Reviews: smí hodnotit
        await Reviews(s => s.SubmitAsync(product.Id, Jan.CustomerId, 4, "Dobrá myš"));
        await ProcessOutboxAsync();   // ProductRatingChangedV1 → kopie katalogu v Orderingu

        var inShop = await Catalog(s => s.GetProductAsync(product.Id));
        Assert.Equal((4m, 1), (inShop!.RatingAverage, inShop.RatingCount));
        var page = await Reviews(s => s.GetForProductAsync(product.Id, Jan.CustomerId));
        Assert.True(page.CanReview);
        Assert.Equal(("Jan N.", 4, "Dobrá myš"), (page.Mine!.AuthorName, page.Mine.Rating, page.Mine.Text));
    }

    [Fact]
    public async Task Hodnoceni_UpravaASkryti_PrumerSePrepocita()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await DeliverApiOrderAsync(Jan, product.Id);
        await DeliverApiOrderAsync(Petra, product.Id);

        await Reviews(s => s.SubmitAsync(product.Id, Jan.CustomerId, 5, null));
        await Reviews(s => s.SubmitAsync(product.Id, Petra.CustomerId, 3, "Ujde to"));
        await Reviews(s => s.SubmitAsync(product.Id, Jan.CustomerId, 1, "Po týdnu rozbitá"));   // úprava, ne druhé hodnocení
        await ProcessOutboxAsync();
        Assert.Equal((2m, 2), await RatingInShopAsync(product.Id));

        var petra = (await Reviews(s => s.GetLatestAsync())).Single(r => r.AuthorName == "Petra M.");
        await Reviews(s => s.SetHiddenAsync(petra.Id, hidden: true));   // moderace
        await ProcessOutboxAsync();

        Assert.Equal((1m, 1), await RatingInShopAsync(product.Id));
        Assert.Single((await Reviews(s => s.GetForProductAsync(product.Id, null))).Reviews);
    }

    [Fact]
    public async Task Hodnoceni_VracenoNoveNejde_NapsaneZustava()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        var order = await DeliveredOrderAsync(product.Id, quantity: 1, PaymentMethod.CashOnDelivery);
        await Reviews(s => s.SubmitAsync(product.Id, Jan.CustomerId, 5, null));

        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nesedí"));
        await ProcessOutboxAsync();
        await Returns(r => r.AcceptGoodsAsync(order.Id));
        await ProcessOutboxAsync();   // ReturnCompletedV1 → Reviews: vráceno

        await Assert.ThrowsAsync<DomainException>(() => Reviews(s => s.SubmitAsync(product.Id, Jan.CustomerId, 1, "Vrátil jsem")));
        Assert.Single((await Reviews(s => s.GetForProductAsync(product.Id, null))).Reviews);
    }

    [Fact]
    public async Task Hodnoceni_StarsiObjednavky_ZeSnimku_AnonymizovaneNe()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await DeliverApiOrderAsync(Jan, product.Id);
        await DeliverApiOrderAsync(Petra, product.Id);
        await Orders(s => s.AnonymizeCustomerAsync(Petra.CustomerId));
        await ProcessOutboxAsync();

        // Modul Reviews „vznikl až teď“ – o objednávkách nic neví.
        await ReviewsDb(async db => { db.PurchasedOrders.RemoveRange(db.PurchasedOrders); await db.SaveChangesAsync(); return true; });
        Assert.True(await Reviews(s => s.SyncPurchasesIfEmptyAsync()));
        await ProcessOutboxAsync();   // SyncPurchasesV1 → Ordering → OrderSnapshotV2 (bez Petry) → kopie nákupů

        Assert.Single(_bus.Published.OfType<OrderSnapshotV2>());
        Assert.True((await Reviews(s => s.GetForProductAsync(product.Id, Jan.CustomerId))).CanReview);
        Assert.False(await Reviews(s => s.SyncPurchasesIfEmptyAsync()));   // podruhé už ne
    }

    [Fact]
    public async Task Hodnoceni_SmazanyUcet_HodnoceniZustaneBezJmena()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await DeliverApiOrderAsync(Jan, product.Id);
        await Reviews(s => s.SubmitAsync(product.Id, Jan.CustomerId, 5, "Doporučuji"));

        await Orders(s => s.AnonymizeCustomerAsync(Jan.CustomerId));
        await ProcessOutboxAsync();   // CustomerAnonymizedV1 → Reviews

        var review = Assert.Single((await Reviews(s => s.GetForProductAsync(product.Id, null))).Reviews);
        Assert.Equal(Review.AnonymizedAuthor, review.AuthorName);
        Assert.False(await ReviewsDb(db => db.PurchasedOrders.AnyAsync()));
    }

    [Fact]
    public async Task Hodnoceni_DoruceniPredbehloVznik_PakSmiHodnotit()
    {
        await RedeliverAsync(new OrderCompletedV1(Guid.NewGuid(), Now.UtcDateTime, 777, Jan.Email), IntegrationModules.Reviews);
        await RedeliverAsync(new OrderPlacedV2(Guid.NewGuid(), Now.UtcDateTime, 777, Jan.CustomerId, Jan.Name, Jan.Email,
            [new OrderItemV2(55, "Myš", 1, 100m, 0m, 100m)], "None", 0m, null, 100m), IntegrationModules.Reviews);

        Assert.True((await Reviews(s => s.GetForProductAsync(55, Jan.CustomerId))).CanReview);
    }

    // ---------- E-shop krok 13: slevové kódy ----------

    [Fact]
    public async Task Kupon_VPokladne_SlevaVPolozkachDopravaZPoSleve_PouzitiZapocteno()
    {
        await CreateCouponAsync("jaro-10", CouponKind.Percent, 10);
        var product = await AddProductAsync(price: 400m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 3));   // 1 200 Kč → po slevě 1 080 (doprava stále zdarma)

        await Checkout(async s => { await s.ApplyCouponAsync(Jan.CustomerId, " jaro-10 "); return true; });
        var summary = await Checkout(s => s.GetAsync(Jan.CustomerId));
        Assert.Equal(("JARO-10", 120m, 1080m), (summary.CouponCode, summary.CouponDiscount, summary.ItemsTotal));

        var order = await Checkout(s => s.PlaceOrderAsync(Courier(summary.CheckoutId), Jan));
        await ProcessOutboxAsync();

        Assert.Equal((1080m, "JARO-10", 120m), (order.TotalPrice, order.CouponCode, order.CouponDiscount));
        var placed = Assert.Single(_bus.Published.OfType<OrderPlacedV2>());   // kontrakt beze změny – ceny už po slevě
        Assert.Equal(1080m, placed.TotalPrice);
        Assert.Equal(1080m, placed.Items.Sum(i => i.LineTotal));
        Assert.Equal(1, (await CouponAsync("JARO-10")).RedemptionCount);
        Assert.Null((await Cart(s => s.GetAsync(Jan.CustomerId))).Lines.FirstOrDefault());
    }

    [Fact]
    public async Task Kupon_DopravaZdarmaSePocitaZCenyPoSleve()
    {
        await CreateCouponAsync("STOVKA", CouponKind.Amount, 100);
        var product = await AddProductAsync(price: 1000m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        await Checkout(async s => { await s.ApplyCouponAsync(Jan.CustomerId, "stovka"); return true; });

        var summary = await Checkout(s => s.GetAsync(Jan.CustomerId));

        Assert.Equal(900m, summary.ItemsTotal);
        Assert.Equal(99m, summary.ShippingOptions.Single(o => o.Method == ShippingMethod.Courier).Price);   // pod 1 000 Kč
    }

    [Fact]
    public async Task Kupon_JedenZakaznikJednou_PoZruseniZnovu()
    {
        await CreateCouponAsync("JEDNOU", CouponKind.Percent, 10);
        var product = await AddProductAsync(price: 100m, stock: 10);
        var order = await PlaceWithCouponAsync(Jan, product.Id, "JEDNOU");

        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        var used = await Assert.ThrowsAsync<DomainException>(() =>
            Checkout(async s => { await s.ApplyCouponAsync(Jan.CustomerId, "JEDNOU"); return true; }));
        Assert.Contains("už použili", used.Message);

        // Kompenzace: zrušená objednávka použití vrátí (ReleaseCouponHandler na OrderCancelled).
        await Orders(s => s.CancelAsync(order.Id, OrderAccess.Customer(Jan.CustomerId)));
        await ProcessOutboxAsync();
        Assert.Equal(0, (await CouponAsync("JEDNOU")).RedemptionCount);
        await Checkout(async s => { await s.ApplyCouponAsync(Jan.CustomerId, "JEDNOU"); return true; });
    }

    [Fact]
    public async Task Kupon_ZamitnutaObjednavka_PouzitiSeVrati()
    {
        await CreateCouponAsync("SKLAD", CouponKind.Percent, 10);
        var product = await AddProductAsync(price: 100m, stock: 1);

        // Petra objedná poslední kus dřív (bez kódu), Jan hned po ní s kódem – sklad rezervuje Petře, Janovi zamítne.
        await Cart(s => s.AddItemAsync(Petra.CustomerId, product.Id, 1));
        var petraCheckout = await Checkout(s => s.GetAsync(Petra.CustomerId));
        await Checkout(s => s.PlaceOrderAsync(Pickup(petraCheckout.CheckoutId), Petra));
        var jan = await PlaceWithCouponAsync(Jan, product.Id, "SKLAD");
        Assert.Equal(1, (await CouponAsync("SKLAD")).RedemptionCount);

        await ProcessOutboxAsync();   // OrderRejected → ReleaseCouponHandler

        Assert.Equal("Rejected", await StatusAsync(jan.Id));
        Assert.Equal(0, (await CouponAsync("SKLAD")).RedemptionCount);
    }

    [Fact]
    public async Task Kupon_PosledniPouziti_DruhaPokladnaDostaneHlasku()
    {
        await CreateCouponAsync("POSLEDNI", CouponKind.Percent, 10, maxRedemptions: 1);
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        await Cart(s => s.AddItemAsync(Petra.CustomerId, product.Id, 1));
        await Checkout(async s => { await s.ApplyCouponAsync(Jan.CustomerId, "POSLEDNI"); return true; });
        await Checkout(async s => { await s.ApplyCouponAsync(Petra.CustomerId, "POSLEDNI"); return true; });   // ještě volný

        var janSummary = await Checkout(s => s.GetAsync(Jan.CustomerId));
        var petraSummary = await Checkout(s => s.GetAsync(Petra.CustomerId));
        await Checkout(s => s.PlaceOrderAsync(Pickup(janSummary.CheckoutId), Jan));

        var exhausted = await Assert.ThrowsAsync<DomainException>(() =>
            Checkout(s => s.PlaceOrderAsync(Pickup(petraSummary.CheckoutId), Petra)));
        Assert.Contains("vyčerpaný", exhausted.Message);
        var after = await Checkout(s => s.GetAsync(Petra.CustomerId));
        Assert.False(after.CanPlaceOrder);                  // nejdřív odebrat kód
        Assert.NotNull(after.CouponProblem);
        Assert.Equal(1, (await CouponAsync("POSLEDNI")).RedemptionCount);
    }

    /// <summary>
    /// Škálování krok 2: dvě pokladny s posledním použitím kuponu opravdu naráz. Nad PostgreSQL (MINISHOP_TEST_DATABASE=Postgres)
    /// běží obě transakce souběžně – projde jen jedna, druhá dostane hlášku a nic neuloží (SQLite zápisy seřadí za sebe).
    /// </summary>
    [Fact]
    public async Task Kupon_PosledniPouzitiNaraz_ProjdeJenJednaObjednavka()
    {
        await CreateCouponAsync("NARAZ", CouponKind.Percent, 10, maxRedemptions: 1);
        var product = await AddProductAsync(price: 100m, stock: 10);
        await Cart(s => s.AddItemAsync(Jan.CustomerId, product.Id, 1));
        await Cart(s => s.AddItemAsync(Petra.CustomerId, product.Id, 1));
        await Checkout(async s => { await s.ApplyCouponAsync(Jan.CustomerId, "NARAZ"); return true; });
        await Checkout(async s => { await s.ApplyCouponAsync(Petra.CustomerId, "NARAZ"); return true; });
        var janCheckout = (await Checkout(s => s.GetAsync(Jan.CustomerId))).CheckoutId;
        var petraCheckout = (await Checkout(s => s.GetAsync(Petra.CustomerId))).CheckoutId;

        var attempts = await Task.WhenAll(
            Attempt(() => Checkout(s => s.PlaceOrderAsync(Pickup(janCheckout), Jan))),
            Attempt(() => Checkout(s => s.PlaceOrderAsync(Pickup(petraCheckout), Petra))));

        Assert.Single(attempts, error => error is null);
        Assert.IsType<DomainException>(Assert.Single(attempts, error => error is not null));
        Assert.Equal(1, await OrderingDb(db => db.Orders.CountAsync()));
        Assert.Equal(1, (await CouponAsync("NARAZ")).RedemptionCount);

        static async Task<Exception?> Attempt(Func<Task> placeOrder)
        {
            try
            {
                await placeOrder();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }

    [Fact]
    public async Task Kupon_Soubeh_VerzeKuponuPustiJenJedenZapis()
    {
        // Dvě pokladny načetly kupon ve stejný okamžik – uložení té druhé narazí na verzi (concurrency token).
        await CreateCouponAsync("SOUBEH", CouponKind.Percent, 10, maxRedemptions: 1);
        await using var first = _services.CreateAsyncScope();
        await using var second = _services.CreateAsyncScope();
        var firstDb = first.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var a = await firstDb.Coupons.SingleAsync(c => c.Code == "SOUBEH");
        var b = await secondDb.Coupons.SingleAsync(c => c.Code == "SOUBEH");

        a.Redeem(100m, Now.UtcDateTime);
        b.Redeem(100m, Now.UtcDateTime);   // obě pokladny vidí „1 volné použití“
        await firstDb.SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondDb.SaveChangesAsync());
        Assert.Equal(1, (await CouponAsync("SOUBEH")).RedemptionCount);
    }

    // ---------- E-shop krok 12: vrácení zboží ----------

    [Fact]
    public async Task Vratka_Kartou_ZboziZpetPenizeZpetPrehledOdecte()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 2, PaymentMethod.Card);
        Assert.Equal(3, await StockAsync());

        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nesedí velikost"));
        await ProcessOutboxAsync();   // OrderReturnRequested → sága vratky + ReturnRequestedV1 → e-mail „pošlete zboží“
        Assert.Equal(ReturnSagaState.AwaitingGoods, (await ReturnSagaAsync(order.Id)).State);
        Assert.Contains(_emails.Sent, e => e.Subject.Contains("pošlete nám zboží"));

        Assert.True(await Returns(r => r.AcceptGoodsAsync(order.Id)));   // admin převzal zásilku
        await ProcessOutboxAsync();   // RestockReturnedGoodsV1 → sklad → RefundPaymentV1 → Payments → PaymentRefundedV1 → konec

        Assert.Equal("Returned", await StatusAsync(order.Id));
        Assert.Equal(5, await StockAsync());
        Assert.Equal(ReturnSagaState.Completed, (await ReturnSagaAsync(order.Id)).State);
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusAsync(order.Id));
        var completed = Assert.Single(_bus.Published.OfType<ReturnCompletedV1>());
        Assert.True(completed.RefundedByCard);
        Assert.Contains(_emails.Sent, e => e.Subject.Contains("je vrácená"));

        var report = await Report(s => s.GetAsync());   // CQRS přehled: vrácená se do tržby nepočítá
        Assert.Equal((1, 0m), (report.StatusCounts[SalesOrderStatus.Returned], report.Today.Revenue));
    }

    [Fact]
    public async Task Vratka_Dobirka_PenizeVraciAdminRucne()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 1, PaymentMethod.CashOnDelivery);

        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nelíbí se mi"));
        await ProcessOutboxAsync();
        await Returns(r => r.AcceptGoodsAsync(order.Id));
        await ProcessOutboxAsync();

        Assert.Equal("Returned", await StatusAsync(order.Id));
        Assert.Equal(5, await StockAsync());
        Assert.Empty(_bus.Published.OfType<RefundPaymentV1>());
        Assert.False(Assert.Single(_bus.Published.OfType<ReturnCompletedV1>()).RefundedByCard);
        Assert.Contains(_emails.Sent, e => e.TextBody.Contains("převodem"));
    }

    [Fact]
    public async Task Vratka_Zamitnuta_ZboziNeNaskladnenoPodruheNejde()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 2, PaymentMethod.Card);
        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nesedí velikost"));
        await ProcessOutboxAsync();

        Assert.True(await Returns(r => r.RejectAsync(order.Id, "Zboží je poškozené")));
        await ProcessOutboxAsync();

        var detail = await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.AllOrders));
        Assert.Equal(("Completed", "Zboží je poškozené"), (detail.Order.Status, detail.ReturnRejectionReason));
        Assert.Null(detail.ReturnableUntilUtc);   // jen jednou
        Assert.Equal(3, await StockAsync());
        Assert.Empty(_bus.Published.OfType<RefundPaymentV1>());
        Assert.Equal("Zboží je poškozené", Assert.Single(_bus.Published.OfType<ReturnRejectedV1>()).Reason);
        await Assert.ThrowsAsync<DomainException>(() =>
            Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Ještě jednou")));
    }

    [Fact]
    public async Task Vratka_ZboziNedoraziloVcas_Vyprsi()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 1, PaymentMethod.Card);
        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nesedí velikost"));
        await ProcessOutboxAsync();

        _time.Advance(ReturnWindow);
        Assert.Equal([order.Id], await ExpiredReturnsAsync());
        Assert.True(await Returns(r => r.TimeOutAsync(order.Id)));
        await ProcessOutboxAsync();

        Assert.Equal("Completed", await StatusAsync(order.Id));
        Assert.Equal(ReturnSaga.ExpiredReason, Assert.Single(_bus.Published.OfType<ReturnRejectedV1>()).Reason);
        Assert.False(await Returns(r => r.AcceptGoodsAsync(order.Id)));   // zásilka po termínu – admin už nic nepřijme
    }

    [Fact]
    public async Task Vratka_PoTerminuNeboCiziNejde()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 1, PaymentMethod.Card);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Petra.CustomerId), "Cizí")));

        Assert.NotNull((await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.AllOrders))).ReturnableUntilUtc);
        _time.Advance(ReturnWindow + TimeSpan.FromMinutes(1));
        Assert.Null((await Orders(s => s.GetDetailAsync(order.Id, OrderAccess.AllOrders))).ReturnableUntilUtc);
        await Assert.ThrowsAsync<DomainException>(() =>
            Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Pozdě")));
    }

    [Fact]
    public async Task Vratka_OpakovanyPrikazSkladu_NenaskladniDvakrat()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 2, PaymentMethod.CashOnDelivery);
        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nesedí velikost"));
        await ProcessOutboxAsync();
        await Returns(r => r.AcceptGoodsAsync(order.Id));
        await ProcessOutboxAsync();

        // Stejný příkaz s jiným ID (inbox ho nepozná) – zachytí ho evidence vrácených rezervací.
        await RedeliverAsync(new RestockReturnedGoodsV1(Guid.NewGuid(), Now.UtcDateTime, order.Id), IntegrationModules.Inventory);
        await ProcessOutboxAsync();

        Assert.Equal(5, await StockAsync());
        Assert.Equal(0, _bus.Published.OfType<ReturnedGoodsRestockedV1>().Last().Quantity);
        Assert.Single(_bus.Published.OfType<ReturnCompletedV1>());   // sága druhou odpověď ignoruje
    }

    // ---------- E-shop krok 11: přehled tržeb (CQRS) ----------

    [Fact]
    public async Task Prehled_ZUdalosti_TrzbaJenZPlatnychObjednavek()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await PlaceConfirmedOrderAsync(product.Id, quantity: 2);
        var cancelled = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await Orders(s => s.CancelAsync(cancelled.Id, OrderAccess.AllOrders));
        await ProcessOutboxAsync();

        var report = await Report(s => s.GetAsync());

        Assert.Equal((1, 200m), (report.Today.OrderCount, report.Today.Revenue));   // zrušená se nepočítá
        Assert.Equal(1, report.StatusCounts[SalesOrderStatus.Confirmed]);
        Assert.Equal(1, report.StatusCounts[SalesOrderStatus.Cancelled]);
        var top = Assert.Single(report.TopProducts);
        Assert.Equal(("Testovací produkt", 2, 200m), (top.Name, top.Quantity, top.Revenue));
        Assert.Equal(SalesReportService.DaysInChart, report.Days.Count);
        Assert.NotNull(report.LastUpdatedAtUtc);
    }

    [Fact]
    public async Task Prehled_ZpravyMimoPoradi_StavNecouvne()
    {
        // Potvrzení a zrušení dorazí dřív než vznik, pak ještě pozdní potvrzení – výsledek je pořád „zrušená“.
        await RedeliverAsync(new OrderConfirmedV1(Guid.NewGuid(), Now.UtcDateTime, 555), IntegrationModules.Reporting);
        await RedeliverAsync(new OrderCancelledV1(Guid.NewGuid(), Now.UtcDateTime, 555, "jan@example.com", []),
            IntegrationModules.Reporting);
        Assert.Equal(0, (await Report(s => s.GetAsync())).Today.OrderCount);   // bez vzniku nevíme kdy ani kolik

        await RedeliverAsync(new OrderPlacedV2(Guid.NewGuid(), Now.UtcDateTime, 555, Jan.CustomerId, Jan.Name, Jan.Email,
            [new OrderItemV2(1, "Myš", 1, 300m, 0m, 300m)], "None", 0m, null, 300m), IntegrationModules.Reporting);
        await RedeliverAsync(new OrderConfirmedV1(Guid.NewGuid(), Now.UtcDateTime, 555), IntegrationModules.Reporting);

        var report = await Report(s => s.GetAsync());
        Assert.Equal(1, report.StatusCounts[SalesOrderStatus.Cancelled]);
        Assert.Equal(0, report.StatusCounts[SalesOrderStatus.Confirmed]);
        Assert.Equal(0m, report.Today.Revenue);
    }

    [Fact]
    public async Task Prehled_Prestaveni_ZeSnimkuStejnyVysledekBezEmailu()
    {
        var product = await AddProductAsync(price: 100m, stock: 10);
        await PlaceConfirmedOrderAsync(product.Id, quantity: 3);
        var cancelled = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await Orders(s => s.CancelAsync(cancelled.Id, OrderAccess.AllOrders));
        await ProcessOutboxAsync();
        var before = await Report(s => s.GetAsync());
        var emailsBefore = _emails.Sent.Count;

        await Report(async s => { await s.RebuildAsync(); return true; });
        Assert.Null((await Report(s => s.GetAsync())).LastUpdatedAtUtc);   // čtecí model je pryč
        await ProcessOutboxAsync();   // RebuildSalesReportV1 → Ordering → OrderSnapshotV1 × 2 → projekce

        var after = await Report(s => s.GetAsync());
        Assert.Equal(2, _bus.Published.OfType<OrderSnapshotV1>().Count());
        Assert.Equal(before.Today, after.Today);
        Assert.Equal(before.TopProducts.Single(), after.TopProducts.Single());
        Assert.Equal(1, after.StatusCounts[SalesOrderStatus.Cancelled]);
        Assert.Equal(emailsBefore, _emails.Sent.Count);   // snímek není „nová objednávka“ – Notifications mlčí
    }

    [Fact]
    public async Task Prehled_PrazdnyPoStartu_PozadaOPrestaveni_PodruheUzNe()
    {
        Assert.True(await Report(s => s.RebuildIfEmptyAsync()));

        var product = await AddProductAsync(price: 100m, stock: 10);
        await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        Assert.False(await Report(s => s.RebuildIfEmptyAsync()));
    }

    // ---------- E-shop krok 7: Notifications ----------

    [Fact]
    public async Task Notifikace_CelaCesta_KazdyStavPraveJedenEmailSOdkazem()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await ShipAsync(order.Id);
        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();
        await ProcessOutboxAsync();   // další kola nic nového nepošlou (inbox)

        Assert.Equal(
            [$"placed:{order.Id}", $"confirmed:{order.Id}", $"shipped:{order.Id}", $"completed:{order.Id}"],
            NotifierEvents);
        var placed = _emails.Sent[0];
        Assert.Equal("jan@example.com", placed.To);
        Assert.Contains("Jan Novák", placed.TextBody);
        Assert.Contains("1 × Testovací produkt", placed.TextBody);
        Assert.Contains($"https://minishop.test/Orders/{order.Id}", placed.TextBody);
        Assert.Contains($"href=\"https://minishop.test/Orders/{order.Id}\"", placed.HtmlBody);
        Assert.Contains("číslo zásilky 123456", _emails.Sent[2].TextBody);
    }

    [Fact]
    public async Task Notifikace_StejnaZpravaDvakrat_JedenEmail()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        var confirmed = Assert.Single(_bus.Published.OfType<OrderConfirmedV1>());

        await RedeliverAsync(confirmed, IntegrationModules.Notifications);   // broker zprávu doručil znovu

        Assert.Single(NotifierEvents, e => e == $"confirmed:{order.Id}");
    }

    [Fact]
    public async Task Notifikace_PotvrzeniPredbehloVznik_ZkusiSeZnovu()
    {
        // Potvrzení objednávky, o které Notifications ještě neví (OrderPlacedV2 nedorazilo) – handler selže,
        // zpráva se zopakuje. Po 5 minutách to vzdá (objednávka z doby před modulem Notifications).
        var early = new OrderConfirmedV1(Guid.NewGuid(), Now.UtcDateTime, 777);

        await Assert.ThrowsAsync<AggregateException>(() => RedeliverAsync(early, IntegrationModules.Notifications));

        _time.Advance(OrderNotificationsHandler.MaxWaitForPlacedOrder);
        await RedeliverAsync(early, IntegrationModules.Notifications);
        Assert.Empty(_emails.Sent);
    }

    [Fact]
    public async Task Notifikace_SmazanyUcet_ZapomeneZakaznika()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await Orders(s => s.CancelAsync(order.Id, OrderAccess.AllOrders));
        await ProcessOutboxAsync();
        Assert.True(await NotificationsDb(db => db.OrderRecipients.AnyAsync(r => r.OrderId == order.Id)));

        await Orders(s => s.AnonymizeCustomerAsync(Jan.CustomerId));
        await ProcessOutboxAsync();   // CustomerAnonymizedV1 → Notifications

        Assert.Single(_bus.Published.OfType<CustomerAnonymizedV1>());
        Assert.False(await NotificationsDb(db => db.OrderRecipients.AnyAsync(r => r.CustomerId == Jan.CustomerId)));
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
        Assert.Contains($"rejected:{order.Id}", NotifierEvents);
        Assert.DoesNotContain($"confirmed:{order.Id}", NotifierEvents);
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

        Assert.Single(NotifierEvents, e => e == $"confirmed:{order.Id}");
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
        Assert.Equal(new[] { "OrderPlacedV1", "OrderPlacedV2", "ReserveStockV1" }, integration.Select(m => m.Type).Order());   // e-shop krok 7: V2
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
        await ShipAsync(completedOrder.Id);
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
                "OrderCancelled", "OrderCancelledV1", "OrderConfirmed", "OrderConfirmedV1", "OrderPlaced", "OrderPlacedV1", "OrderPlacedV2",
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
        // Škálování krok 2: přes EF (ne ruční SQL) – stejný příkaz pro SQLite i PostgreSQL (schéma, uvozovky).
        await OrderingDb(db => db.CatalogProducts.ExecuteUpdateAsync(setters => setters
            .SetProperty(p => p.Availability, CatalogAvailability.Unknown)
            .SetProperty(p => p.CategoryId, (int?)null)
            .SetProperty(p => p.CategoryName, (string?)null)));

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
        // SQLite: každý modul vlastní soubor. PostgreSQL (škálování krok 1): jedna databáze, ale každý modul vlastní schéma.
        var ordering = await OrderingDb(db => Task.FromResult((db.Database.GetConnectionString(), db.Model.GetDefaultSchema())));
        var inventory = await InventoryDb(db => Task.FromResult((db.Database.GetConnectionString(), db.Model.GetDefaultSchema())));
        Assert.NotEqual(ordering, inventory);
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

        var noAddress = new CheckoutRequest { CheckoutId = checkout.CheckoutId, ShippingMethod = ShippingMethod.Courier, PaymentMethod = PaymentMethod.CashOnDelivery };
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
    /// E-shop krok 7: e-maily, které modul Notifications „odeslal“, jako krátké záznamy „confirmed:5“, „shipped:5“…
    /// (podle předmětu šablony) – testy se tak ptají na typ oznámení, ne na přesné znění.
    /// </summary>
    private IReadOnlyList<string> NotifierEvents => _emails.Sent.Select(email =>
    {
        var orderId = System.Text.RegularExpressions.Regex.Match(email.Subject, @"č\. (\d+)").Groups[1].Value;
        var kind = email.Subject switch
        {
            var s when s.Contains("jsme přijali") => "placed",
            var s when s.Contains("je potvrzená") => "confirmed",
            var s when s.Contains("nelze vyřídit") => "rejected",
            var s when s.Contains("je na cestě") || s.Contains("k vyzvednutí") => "shipped",
            var s when s.Contains("je doručená") || s.Contains("je vyzvednutá") => "completed",
            var s when s.Contains("je zrušená") => "cancelled",
            _ => email.Subject
        };
        return $"{kind}:{orderId}";
    }).ToList();

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

    /// <summary>E-shop krok 8: objednávka z pokladny (osobní odběr) s platbou kartou (nebo jinou).</summary>
    private async Task<OrderDto> PlaceCardOrderAsync(int productId, int quantity, PaymentMethod payment = PaymentMethod.Card)
    {
        await Cart(s => s.AddItemAsync(Jan.CustomerId, productId, quantity));
        var checkout = await Checkout(s => s.GetAsync(Jan.CustomerId));
        var request = Pickup(checkout.CheckoutId);
        request.PaymentMethod = payment;
        return await Checkout(s => s.PlaceOrderAsync(request, Jan));
    }

    /// <summary>
    /// E-shop krok 8: zákazník na stránce simulované brány klikl Zaplatit / Zamítnout. <paramref name="ignoreExpiry"/>
    /// = webhook skutečné brány, který přišel po termínu (simulace by po termínu zaplatit nepustila).
    /// </summary>
    private async Task<PaymentWebhookResult> SimulateGatewayAsync(int orderId, bool paid, bool ignoreExpiry = false)
    {
        await using var scope = _services.CreateAsyncScope();
        var payment = await scope.ServiceProvider.GetRequiredService<IPaymentRepository>().GetByOrderIdAsync(orderId)
            ?? throw new InvalidOperationException($"Platba objednávky {orderId} nevznikla.");
        Assert.True(ignoreExpiry || !payment.IsExpired(_time.GetUtcNow().UtcDateTime), "Platba už vypršela.");
        var gateway = scope.ServiceProvider.GetRequiredService<FakePaymentGatewaySimulator>();
        return paid ? await gateway.PayAsync(payment.Id) : await gateway.DeclineAsync(payment.Id);
    }

    /// <summary>E-shop krok 6: zabalit a odeslat (kurýr PPL) – každý krok jako samostatný požadavek admina.</summary>
    private async Task ShipAsync(int orderId)
    {
        await Orders(s => s.StartPackingAsync(orderId));
        await Orders(s => s.ShipAsync(orderId, "PPL", "123456"));
    }

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

    private async Task<T> NotificationsDb<T>(Func<NotificationsDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>());
    }

    /// <summary>E-shop krok 7: doručí zprávu modulu ještě jednou (jako broker po výpadku) – ve vlastním scope.</summary>
    private async Task RedeliverAsync(IIntegrationMessage message, string module)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntegrationMessageDispatcher>().DispatchAsync(message, module);
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

    /// <summary>E-shop krok 10: Jan má účet s e-mailem a (ne)souhlasem s připomínkou košíku.</summary>
    private void AllowReminders(bool consent, bool emailConfirmed = true) =>
        _contacts.Set(Jan.CustomerId, new CustomerContact(Jan.Email, Jan.Name, emailConfirmed, consent));

    /// <summary>E-shop krok 10: jedno kolo CartReminderWorkeru – najít opuštěné košíky, každý ve vlastním scope.</summary>
    private async Task RunCartReminderWorkerAsync()
    {
        foreach (var cartId in await Reminders(s => s.GetAbandonedCartIdsAsync()))
        {
            await Reminders(s => s.RemindAsync(cartId));
        }
    }

    /// <summary>E-shop krok 14: hodnocení (modul Reviews) ve vlastním scope.</summary>
    private async Task<T> Reviews<T>(Func<ReviewService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ReviewService>());
    }

    private async Task Reviews(Func<ReviewService, Task> action) => await Reviews(async s => { await action(s); return true; });

    private async Task<T> ReviewsDb<T>(Func<ReviewsDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ReviewsDbContext>());
    }

    private async Task<T> Catalog<T>(Func<ICatalogQueryService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICatalogQueryService>());
    }

    private async Task<(decimal Average, int Count)> RatingInShopAsync(int productId) =>
        await Catalog(s => s.GetProductAsync(productId)) is { } p ? (p.RatingAverage, p.RatingCount) : (0m, 0);

    /// <summary>E-shop krok 14: objednávka přes API (bez pokladny) doručená zákazníkovi – smí hodnotit.</summary>
    private async Task DeliverApiOrderAsync(OrderCustomer customer, int productId)
    {
        var order = await PlaceConfirmedOrderAsync(productId, quantity: 1, customer);
        await ShipAsync(order.Id);
        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();
    }

    /// <summary>E-shop krok 13: admin založí kupon (správa kuponů ve vlastním scope).</summary>
    private async Task CreateCouponAsync(string code, CouponKind kind, decimal value, int? maxRedemptions = null)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CouponService>().CreateAsync(new CreateCouponRequest
        {
            Code = code, Kind = kind, Value = value, MaxRedemptions = maxRedemptions
        });
    }

    private Task<Coupon> CouponAsync(string code) => OrderingDb(db => db.Coupons.AsNoTracking().SingleAsync(c => c.Code == code));

    /// <summary>E-shop krok 13: zákazník dá produkt do košíku, uplatní kód a objedná (osobní odběr, dobírka).</summary>
    private async Task<OrderDto> PlaceWithCouponAsync(OrderCustomer customer, int productId, string code)
    {
        await Cart(s => s.AddItemAsync(customer.CustomerId, productId, 1));
        await Checkout(async s => { await s.ApplyCouponAsync(customer.CustomerId, code); return true; });
        var summary = await Checkout(s => s.GetAsync(customer.CustomerId));
        return await Checkout(s => s.PlaceOrderAsync(Pickup(summary.CheckoutId), customer));
    }

    /// <summary>E-shop krok 12: doručená objednávka z pokladny (kartou zaplacená) – podklad pro vratku.</summary>
    private async Task<OrderDto> DeliveredOrderAsync(int productId, int quantity, PaymentMethod payment)
    {
        var order = await PlaceCardOrderAsync(productId, quantity, payment);
        await ProcessOutboxAsync();
        if (payment == PaymentMethod.Card)
        {
            await SimulateGatewayAsync(order.Id, paid: true);
            await ProcessOutboxAsync();
        }

        await ShipAsync(order.Id);
        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();
        Assert.Equal("Completed", await StatusAsync(order.Id));
        return order;
    }

    /// <summary>E-shop krok 12: orchestrátor vratky ve vlastním scope – jako stránka Vratky nebo timeout worker.</summary>
    private async Task<T> Returns<T>(Func<ReturnSagaOrchestrator, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ReturnSagaOrchestrator>());
    }

    private Task<ReturnSaga> ReturnSagaAsync(int orderId) => OrderingDb(db => db.ReturnSagas.SingleAsync(s => s.OrderId == orderId));

    private async Task<IReadOnlyList<int>> ExpiredReturnsAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReturnSagaRepository>()
            .GetExpiredOrderIdsAsync(_time.GetUtcNow().UtcDateTime);
    }

    /// <summary>E-shop krok 11: přehled tržeb (čtecí strana modulu Reporting) ve vlastním scope.</summary>
    private async Task<T> Report<T>(Func<SalesReportService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<SalesReportService>());
    }

    private async Task<T> Reminders<T>(Func<CartReminderService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CartReminderService>());
    }

    /// <summary>E-shop krok 9: stav platby v modulu Payments (čerstvý scope).</summary>
    private async Task<PaymentStatus> PaymentStatusAsync(int orderId)
    {
        await using var scope = _services.CreateAsyncScope();
        var payment = await scope.ServiceProvider.GetRequiredService<IPaymentRepository>().GetByOrderIdAsync(orderId);
        return payment!.Status;
    }

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
        new() { CheckoutId = checkoutId, ShippingMethod = ShippingMethod.Pickup, PaymentMethod = PaymentMethod.CashOnDelivery };

    private static CheckoutRequest Courier(Guid checkoutId) => new()
    {
        CheckoutId = checkoutId,
        ShippingMethod = ShippingMethod.Courier,
        PaymentMethod = PaymentMethod.CashOnDelivery,   // e-shop krok 8: dobírka – sága platbu přeskočí (jako dřív)
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
        _databases.Dispose();
    }
}
