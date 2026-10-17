using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Krok 4: události se ukládají do outboxu ve stejné transakci jako data.
/// Krok 5: OutboxProcessor je doručí, při chybě zkouší znovu a po 5 neúspěších to vzdá (poison message).
/// </summary>
public sealed class OutboxTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDatabase _ordering = new();
    private readonly InMemoryDatabase _inventory = new();
    private int _nextProductId = 1;
    private readonly SwitchableDispatcher _dispatcher = new();
    private readonly RecordingIntegrationBus _bus = new();
    private readonly ServiceProvider _services;

    /// <summary>Dispatcher, který si zapisuje doručené události a na povel selže – simuluje chybu handleru.</summary>
    private sealed class SwitchableDispatcher : IDomainEventDispatcher
    {
        public bool Fail { get; set; }
        public List<IDomainEvent> Delivered { get; } = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Sklad nedostupný");
            }

            Delivered.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    public OutboxTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddInfrastructure(_ordering.ConnectionString, _inventory.ConnectionString);
        services.AddSingleton<IDomainEventDispatcher>(_dispatcher);   // přebije skutečný dispatcher
        services.AddSingleton<IIntegrationBus>(_bus);            // a "broker"

        _services = services.BuildServiceProvider();
        Db(db => db.Database.EnsureCreatedAsync()).GetAwaiter().GetResult();
    }

    /// <summary>Pošťák outboxu modulu Ordering (krok 10: každý modul má vlastní).</summary>
    private IOutboxProcessor Processor =>
        _services.GetServices<IOutboxProcessor>().OfType<OutboxProcessor<OrderingDbContext>>().Single();

    [Fact]
    public async Task Ulozeni_ZapiseUdalostDoOutboxuSDatyObjednavky()
    {
        var orderId = await PlaceOrderAsync(quantity: 3);

        var message = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Equal("OrderPlaced", message.Type);
        Assert.Equal(Now.UtcDateTime, message.OccurredAtUtc);

        // Payload jsou jen data, ne entita – a objednávka v nich už má ID.
        var payload = JsonSerializer.Deserialize<OrderPayload>(message.Payload)!;
        Assert.Equal(orderId, payload.OrderId);
        Assert.Equal("jan@example.com", payload.CustomerEmail);
        var line = Assert.Single(payload.Lines);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public async Task Ulozeni_UdalostHnedNerozesila()
    {
        await PlaceOrderAsync(quantity: 1);

        var message = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.Attempts);
        Assert.Empty(_dispatcher.Delivered);
    }

    [Fact]
    public async Task UlozeniBezUdalosti_OutboxNemeni()
    {
        await AddProductAsync();

        Assert.Empty(await Db(db => db.OutboxMessages.ToListAsync()));
    }

    [Fact]
    public async Task Processor_DoruciUdalostZNoveNactenouObjednavkouAOznaciJi()
    {
        var orderId = await PlaceOrderAsync(quantity: 2);

        var processed = await Processor.ProcessPendingAsync();

        Assert.Equal(1, processed);
        var placed = Assert.IsType<OrderPlaced>(Assert.Single(_dispatcher.Delivered));
        Assert.Equal(orderId, placed.Order.Id);
        Assert.Equal(2, placed.Order.Items.Single().Quantity);
        Assert.Equal(Now.UtcDateTime, placed.OccurredAtUtc);

        var message = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Equal(Now.UtcDateTime, message.ProcessedAtUtc);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task Processor_ZpracovanouZpravuUzZnovuNedoruci()
    {
        await PlaceOrderAsync(quantity: 1);
        await Processor.ProcessPendingAsync();

        var processedAgain = await Processor.ProcessPendingAsync();

        Assert.Equal(0, processedAgain);
        Assert.Single(_dispatcher.Delivered);
    }

    [Fact]
    public async Task Processor_ChybaHandleru_ZvysiPocetPokusuAZkusiZnovu()
    {
        await PlaceOrderAsync(quantity: 1);
        _dispatcher.Fail = true;

        await Processor.ProcessPendingAsync();

        var failed = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Null(failed.ProcessedAtUtc);
        Assert.Equal(1, failed.Attempts);
        Assert.Contains("Sklad nedostupný", failed.Error);

        // Sklad je zase dostupný – další kolo zprávu doručí a chybu smaže.
        _dispatcher.Fail = false;
        await Processor.ProcessPendingAsync();

        var retried = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.NotNull(retried.ProcessedAtUtc);
        Assert.Null(retried.Error);
        Assert.Single(_dispatcher.Delivered);
    }

    [Fact]
    public async Task Processor_PoMaxPoctuPokusu_ZpravuVzda()
    {
        await PlaceOrderAsync(quantity: 1);
        _dispatcher.Fail = true;

        for (var i = 0; i < OutboxProcessor.MaxAttempts; i++)
        {
            Assert.Equal(1, await Processor.ProcessPendingAsync());
        }

        // Poison message: zůstane v tabulce nezpracovaná, ale už se nezkouší.
        _dispatcher.Fail = false;
        Assert.Equal(0, await Processor.ProcessPendingAsync());

        var message = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(OutboxProcessor.MaxAttempts, message.Attempts);
        Assert.Empty(_dispatcher.Delivered);
    }

    [Fact]
    public async Task Processor_NeexistujiciObjednavka_ZapiseChybu()
    {
        await Db(db =>
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = nameof(OrderCancelled),
                Payload = JsonSerializer.Serialize(new OrderPayload(999, "x@example.com", 0m, [])),
                OccurredAtUtc = Now.UtcDateTime
            });
            return db.SaveChangesAsync();
        });

        await Processor.ProcessPendingAsync();

        var message = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Equal(1, message.Attempts);
        Assert.Contains("999", message.Error);
    }

    [Fact]
    public async Task Processor_NedostupnyBroker_ZpravaCekaANepocitaSeJakoPokus()
    {
        await AddIntegrationMessageAsync(orderId: 1);
        _bus.FailWith = new IntegrationBusUnavailableException("Broker neběží", new IOException());

        // Výpadek delší než MaxAttempts kol – zpráva přesto nepropadne jako poison message.
        for (var i = 0; i < OutboxProcessor.MaxAttempts + 2; i++)
        {
            await Processor.ProcessPendingAsync();
        }

        var waiting = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Null(waiting.ProcessedAtUtc);
        Assert.Equal(0, waiting.Attempts);
        Assert.Contains("Broker neběží", waiting.Error);

        // Broker je zpátky – zpráva odejde.
        _bus.FailWith = null;
        await Processor.ProcessPendingAsync();

        var sent = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.NotNull(sent.ProcessedAtUtc);
        Assert.Null(sent.Error);
        Assert.Single(_bus.Published);
    }

    [Fact]
    public async Task Processor_NedostupnyBroker_DalsiIntegracniPreskociDomenoveZpracuje()
    {
        await AddIntegrationMessageAsync(orderId: 1);
        await AddIntegrationMessageAsync(orderId: 2);
        await PlaceOrderAsync(quantity: 1);
        _bus.FailWith = new IntegrationBusUnavailableException("Broker neběží", new IOException());

        var processed = await Processor.ProcessPendingAsync();

        // Zkusila se jen první integrační (pak už je jasné, že broker neběží) + doménová.
        Assert.Equal(2, processed);
        Assert.Single(_dispatcher.Delivered);
        var integration = await Db(db => db.OutboxMessages
            .Where(m => m.Kind == OutboxMessageKind.Integration).ToListAsync());
        Assert.All(integration, m => Assert.Null(m.ProcessedAtUtc));
        Assert.Single(integration, m => m.Error is not null);
    }

    [Fact]
    public async Task Processor_JinaChybaBusu_PocitaSeJakoPokus()
    {
        await AddIntegrationMessageAsync(orderId: 1);
        _bus.FailWith = new InvalidOperationException("Broker zprávu odmítl");

        await Processor.ProcessPendingAsync();

        var message = await Db(db => db.OutboxMessages.SingleAsync());
        Assert.Equal(1, message.Attempts);
        Assert.Contains("odmítl", message.Error);
    }

    private Task<int> AddIntegrationMessageAsync(int orderId) => Db(db =>
    {
        db.OutboxMessages.Add(OutboxMessageFactory.Create(
            new OrderCompletedV1(Guid.NewGuid(), Now.UtcDateTime, orderId, "jan@example.com")));
        return db.SaveChangesAsync();
    });

    private async Task<int> PlaceOrderAsync(int quantity)
    {
        var product = await AddProductAsync();

        return await Db(async db =>
        {
            var order = new Order("Jan Novák", "jan@example.com", Now.UtcDateTime);
            order.AddItem(product, quantity, discount: 0m);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.Id;
        });
    }

    private Task<CatalogProduct> AddProductAsync() => Db(async db =>
    {
        var product = new CatalogProduct(_nextProductId++, "Testovací produkt", 100m, Now.UtcDateTime);
        db.CatalogProducts.Add(product);
        await db.SaveChangesAsync();
        return product;
    });

    private async Task<T> Db<T>(Func<OrderingDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<OrderingDbContext>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _ordering.Dispose();
        _inventory.Dispose();
    }
}
