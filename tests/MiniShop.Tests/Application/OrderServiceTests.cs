using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Contracts;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Inventory;
using MiniShop.Application.Orders.EventHandlers;
using MiniShop.Application.Orders.IntegrationEvents;
using MiniShop.Application.Orders.Saga;
using MiniShop.Application.Pricing;
using MiniShop.Application.Services;
using MiniShop.Contracts;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Domain.Products;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;
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

    private readonly InMemoryDatabase _database = new();
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
        services.AddInfrastructure(_database.ConnectionString);

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

        // Sága (orchestrátor) a modul Inventory.
        services.AddSingleton(new OrderSagaSettings(ReservationTimeout));
        services.AddDomainEventHandler<OrderPlaced, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCompleted, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCancelled, OrderSagaOrchestrator>();
        services.AddIntegrationMessageHandler<StockReservedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReservationFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReleasedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ReserveStockV1, ReserveStockHandler>(IntegrationModules.Inventory);
        services.AddIntegrationMessageHandler<ReleaseStockV1, ReleaseStockHandler>(IntegrationModules.Inventory);

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
        Db(db => db.Database.EnsureCreatedAsync()).GetAwaiter().GetResult();
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
        var reservation = Assert.Single(await Db(db => db.StockReservations.ToListAsync()));
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
        Assert.Empty(await Db(db => db.StockReservations.ToListAsync()));
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
        Assert.All(await Db(db => db.StockReservations.ToListAsync()), r => Assert.NotNull(r.ReleasedAtUtc));
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

        var message = await Db(db => db.OutboxMessages.SingleAsync(m => m.Type == "OrderCancelled"));
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
        var integration = await Db(db => db.OutboxMessages.Where(m => m.Kind == OutboxMessageKind.Integration).ToListAsync());
        Assert.Equal(new[] { "OrderPlacedV1", "ReserveStockV1" }, integration.Select(m => m.Type).Order());
        Assert.All(integration, m => Assert.Null(m.ProcessedAtUtc));
        Assert.Empty(_bus.Published);

        // 2. kolo: worker je pošle přes bus. Doménové handlery je nedostanou.
        await ProcessOneRoundAsync();
        Assert.Equal(2, _bus.Published.Count);
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

        var messages = await Db(db => db.OutboxMessages.ToListAsync());
        Assert.All(messages, m => Assert.Equal(Now.UtcDateTime, m.ProcessedAtUtc));
        Assert.Equal(
            new[]
            {
                "OrderCancelled", "OrderCancelledV1", "OrderConfirmed", "OrderPlaced", "OrderPlacedV1",
                "ReleaseStockV1", "ReserveStockV1", "StockReleasedV1", "StockReservedV1"
            },
            messages.Select(m => m.Type).Order());
    }

    // ---------- Pomocné metody ----------

    /// <summary>
    /// Zpracuje outbox jako několik kol OutboxWorkeru: pokračuje, dokud byla dávka plná nebo handlery
    /// přidaly nové zprávy (sága = řetěz zpráv). Pozor: v dalších kolech se zkusí znovu
    /// i zprávy, které selhaly – pro jedno kolo je <see cref="ProcessOneRoundAsync"/>.
    /// </summary>
    private async Task ProcessOutboxAsync()
    {
        var processor = _services.GetRequiredService<OutboxProcessor>();
        bool again;
        do
        {
            var before = await Db(db => db.OutboxMessages.CountAsync());
            var processed = await processor.ProcessPendingAsync();
            again = processed == OutboxProcessor.BatchSize || await Db(db => db.OutboxMessages.CountAsync()) > before;
        }
        while (again);
    }

    /// <summary>Právě jedno kolo workera (jedna dávka).</summary>
    private Task<int> ProcessOneRoundAsync() => _services.GetRequiredService<OutboxProcessor>().ProcessPendingAsync();

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

    /// <summary>Přímý přístup k DB ve vlastním scope (čerstvý DbContext, žádná data z cache).</summary>
    private async Task<T> Db<T>(Func<MiniShopDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<MiniShopDbContext>());
    }

    private async Task<IReadOnlyList<int>> ExpiredSagasAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOrderSagaRepository>()
            .GetExpiredOrderIdsAsync(_time.GetUtcNow().UtcDateTime);
    }

    private async Task<string> StatusAsync(int orderId) => (await Orders(s => s.GetByIdAsync(orderId))).Status;

    private Task<OrderSaga> SagaAsync(int orderId) => Db(db => db.OrderSagas.SingleAsync(s => s.OrderId == orderId));

    private Task<int> StockAsync() => Db(async db => (await db.Products.SingleAsync()).StockQuantity);

    private Task<int> StockAsync(int productId) =>
        Db(async db => (await db.Products.SingleAsync(p => p.Id == productId)).StockQuantity);

    private Task<Product> AddProductAsync(decimal price, int stock) => Db(async db =>
    {
        var product = new Product("Testovací produkt", null, price, stock);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    });

    private static CreateOrderRequest CreateRequest(params (int ProductId, int Quantity)[] items) => new()
    {
        CustomerName = "Jan Novák",
        CustomerEmail = "jan@example.com",
        Items = items.Select(i => new CreateOrderItemRequest { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
    };

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }
}
