using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Contracts;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Orders.EventHandlers;
using MiniShop.Application.Orders.IntegrationEvents;
using MiniShop.Application.Pricing;
using MiniShop.Application.Services;
using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Products;
using MiniShop.Infrastructure;
using MiniShop.Contracts.Orders;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Application;

/// <summary>
/// Testy OrderService nad skutečnou SQLite databází v paměti.
/// Skládají stejné služby jako aplikace (AddInfrastructure + handlery z kroku 3) a každá akce běží
/// ve vlastním scope jako HTTP požadavek. Od kroku 5 handlery neběží hned – test proto po akci
/// "zavolá pošťáka" (<see cref="ProcessOutboxAsync"/>), stejně jako by to udělal OutboxWorker.
/// </summary>
public sealed class OrderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDatabase _database = new();
    private readonly RecordingOrderNotifier _notifier = new();
    private readonly RecordingIntegrationEventBus _bus = new();
    private readonly List<IDomainEvent> _dispatched = new();
    private readonly ServiceProvider _services;

    public OrderServiceTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddInfrastructure(_database.ConnectionString);

        // Stejné služby a handlery jako v aplikaci (ApplicationServiceCollectionExtensions).
        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton<IDiscountPolicy>(new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m));
        services.AddDomainEventHandler<OrderPlaced, SendOrderConfirmationHandler>();
        services.AddDomainEventHandler<OrderCompleted, SendCompletionNoticeHandler>();
        services.AddDomainEventHandler<OrderCancelled, ReturnStockOnOrderCancelledHandler>();
        services.AddDomainEventHandler<OrderCancelled, SendCancellationNoticeHandler>();
        services.AddDomainEventHandler<OrderPlaced, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCompleted, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCancelled, PublishOrderIntegrationEventsHandler>();

        // Testovací náhrady: notifikace a "broker" do seznamu, skutečný dispatcher obalený záznamníkem.
        services.AddSingleton<IOrderNotifier>(_notifier);
        services.AddSingleton<IIntegrationEventBus>(_bus);
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<IDomainEventDispatcher>(sp =>
            new RecordingDomainEventDispatcher(_dispatched, sp.GetRequiredService<DomainEventDispatcher>()));

        _services = services.BuildServiceProvider();
        Db(db => db.Database.EnsureCreatedAsync()).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task PlaceOrder_OdecteZasobuAUlozObjednavku()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        Assert.True(order.Id > 0);
        Assert.Equal(300m, order.TotalPrice);
        Assert.Equal(Now.UtcDateTime, order.CreatedAtUtc);
        Assert.Equal(2, await StockAsync());
        Assert.Contains($"placed:{order.Id}", _notifier.Events);
    }

    [Fact]
    public async Task PlaceOrder_PredZpracovanimOutboxu_HandleryJesteNeprobehly()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        // Krok 5: požadavek skončil hned po uložení, událost čeká v outboxu na workera.
        Assert.Empty(_notifier.Events);
        var message = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Null(message.ProcessedAtUtc);
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
    public async Task PlaceOrder_NedostatekZbozi_NicSeNeulozi()
    {
        var product = await AddProductAsync(price: 100m, stock: 2);

        await Assert.ThrowsAsync<InsufficientStockException>(
            () => Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 3)))));
        await ProcessOutboxAsync();

        Assert.Empty(await Db(db => db.Orders.ToListAsync()));
        Assert.Empty(await Db(db => db.OutboxMessages.ToListAsync()));
        Assert.Empty(_notifier.Events);
        Assert.Empty(_dispatched);
    }

    [Fact]
    public async Task PlaceOrder_NeexistujiciProdukt_VyhodiNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Orders(s => s.PlaceOrderAsync(CreateRequest((999, 1)))));
    }

    [Fact]
    public async Task PlaceOrder_WorkerRozesleOrderPlacedSeZnamymId()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));
        await ProcessOutboxAsync();

        var placed = Assert.Single(_dispatched.OfType<OrderPlaced>());
        Assert.Equal(order.Id, placed.Order.Id);
        Assert.Single(placed.Order.Items);   // objednávka znovu načtená z DB i s položkami
    }

    [Fact]
    public async Task Cancel_VratiZboziNaSklad()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 5))));

        var cancelled = await Orders(s => s.CancelAsync(order.Id));
        Assert.Equal(0, await StockAsync());   // eventual consistency: zboží se ještě nevrátilo

        await ProcessOutboxAsync();

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(5, await StockAsync());
        Assert.Contains($"cancelled:{order.Id}", _notifier.Events);
    }

    [Fact]
    public async Task Cancel_VyrizenouObjednavku_VyhodiDomainException()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));
        await Orders(s => s.CompleteAsync(order.Id));

        await Assert.ThrowsAsync<DomainException>(() => Orders(s => s.CancelAsync(order.Id)));
    }

    [Fact]
    public async Task Cancel_WorkerRozesleOrderCancelled()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        await Orders(s => s.CancelAsync(order.Id));
        await ProcessOutboxAsync();

        var cancelled = Assert.Single(_dispatched.OfType<OrderCancelled>());
        Assert.Equal(order.Id, cancelled.Order.Id);
    }

    [Fact]
    public async Task Cancel_UdalostiVOutboxuJsouPoZpracovaniOznacene()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 2))));
        await Orders(s => s.CancelAsync(order.Id));

        await ProcessOutboxAsync();

        // Čas je v testu pevný, takže pořadí podle OccurredAtUtc nerozhodne – porovnáme seřazené typy.
        // Ke každé doménové události přibyla integrační (krok 7).
        var messages = await Db(db => db.OutboxMessages.ToListAsync());
        Assert.Equal(new[] { "OrderCancelled", "OrderCancelledV1", "OrderPlaced", "OrderPlacedV1" },
            messages.Select(m => m.Type).Order());
        Assert.All(messages, m => Assert.Equal(Now.UtcDateTime, m.ProcessedAtUtc));
    }

    [Fact]
    public async Task Complete_PosleOznameniPresHandler()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();

        Assert.Contains($"completed:{order.Id}", _notifier.Events);
        Assert.Equal(4, await StockAsync());   // vyřízení sklad nemění
    }

    [Fact]
    public async Task Cancel_SmazanyProdukt_HandlerHoPreskoci()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 2))));
        await Db(async db =>
        {
            db.Products.Remove(await db.Products.SingleAsync());
            return await db.SaveChangesAsync();
        });

        var cancelled = await Orders(s => s.CancelAsync(order.Id));
        await ProcessOutboxAsync();

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Contains($"cancelled:{order.Id}", _notifier.Events);   // ostatní handlery proběhly
        Assert.All(await Db(db => db.OutboxMessages.ToListAsync()), m => Assert.Null(m.Error));
    }

    [Fact]
    public async Task Cancel_NotifikacePoprveSelze_PriOpakovaniSeZboziNevratiDvakrat()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 2))));
        _notifier.CancellationFailuresLeft = 1;

        await Orders(s => s.CancelAsync(order.Id));

        // 1. kolo workera: vrácení skladu uspěje, notifikace selže → zpráva zůstane nezpracovaná.
        await ProcessOneRoundAsync();
        Assert.Equal(5, await StockAsync());
        Assert.DoesNotContain($"cancelled:{order.Id}", _notifier.Events);

        // 2. kolo: inbox vrácení skladu přeskočí (krok 6), notifikace tentokrát projde.
        await ProcessOutboxAsync();
        Assert.Equal(5, await StockAsync());   // bez inboxu by tu bylo 7
        Assert.Contains($"cancelled:{order.Id}", _notifier.Events);

        var message = await Db(db => db.OutboxMessages.SingleAsync(m => m.Type == "OrderCancelled"));
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Equal(1, message.Attempts);

        // I překladový handler (krok 7) chrání inbox – integrační událost odešla jen jednou.
        Assert.Single(_bus.Published.OfType<OrderCancelledV1>());
    }

    [Fact]
    public async Task PlaceOrder_WorkerPublikujeOrderPlacedV1SDatyObjednavky()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 3))));
        await ProcessOutboxAsync();

        var placed = Assert.IsType<OrderPlacedV1>(Assert.Single(_bus.Published));
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal("jan@example.com", placed.CustomerEmail);
        Assert.Equal(300m, placed.TotalPrice);
        Assert.Equal(Now.UtcDateTime, placed.OccurredAtUtc);   // kdy se fakt stal, ne kdy odešla zpráva
        Assert.Equal(new OrderLineV1(product.Id, 3), Assert.Single(placed.Lines));
    }

    [Fact]
    public async Task PlaceOrder_IntegracniUdalostJeVOutboxuJakoSamostatnaZprava()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        // 1. kolo: handlery doménové události zapíšou integrační do outboxu – ven ještě neodešla.
        await ProcessOneRoundAsync();
        var integration = await Db(db => db.OutboxMessages.SingleAsync(m => m.Kind == OutboxMessageKind.Integration));
        Assert.Equal("OrderPlacedV1", integration.Type);
        Assert.Null(integration.ProcessedAtUtc);
        Assert.Empty(_bus.Published);

        // 2. kolo: worker ji pošle ven. Handlery v aplikaci ji nedostanou (dispatcher zná jen doménové události).
        await ProcessOneRoundAsync();
        Assert.Single(_bus.Published);
        Assert.Single(_dispatched);
        Assert.All(await Db(db => db.OutboxMessages.ToListAsync()), m => Assert.NotNull(m.ProcessedAtUtc));
    }

    [Fact]
    public async Task CancelAComplete_WorkerPublikujeIntegracniUdalosti()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var cancelledOrder = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 2))));
        var completedOrder = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1))));

        await Orders(s => s.CancelAsync(cancelledOrder.Id));
        await Orders(s => s.CompleteAsync(completedOrder.Id));
        await ProcessOutboxAsync();

        var cancelled = Assert.Single(_bus.Published.OfType<OrderCancelledV1>());
        Assert.Equal(cancelledOrder.Id, cancelled.OrderId);
        Assert.Equal(new OrderLineV1(product.Id, 2), Assert.Single(cancelled.Lines));   // sklad ví, co vrátit

        var completed = Assert.Single(_bus.Published.OfType<OrderCompletedV1>());
        Assert.Equal(completedOrder.Id, completed.OrderId);
        Assert.Equal("jan@example.com", completed.CustomerEmail);

        Assert.Equal(2, _bus.Published.OfType<OrderPlacedV1>().Count());
    }

    /// <summary>
    /// Zpracuje outbox jako několik kol OutboxWorkeru: pokračuje, dokud byla dávka plná nebo handlery
    /// přidaly nové zprávy (integrační události z kroku 7). Pozor: v dalších kolech se zkusí znovu
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

    /// <summary>Zavolá OrderService ve vlastním scope – jako jeden HTTP požadavek.</summary>
    private async Task<T> Orders<T>(Func<IOrderService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IOrderService>());
    }

    /// <summary>Přímý přístup k DB ve vlastním scope (čerstvý DbContext, žádná data z cache).</summary>
    private async Task<T> Db<T>(Func<MiniShopDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<MiniShopDbContext>());
    }

    private Task<int> StockAsync() => Db(async db => (await db.Products.SingleAsync()).StockQuantity);

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
