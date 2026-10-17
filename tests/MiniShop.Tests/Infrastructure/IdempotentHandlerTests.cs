using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Products;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Inbox;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Krok 6 EDA: dekorátor IdempotentDomainEventHandler zajistí, že handler zpracuje
/// stejnou událost (stejné EventId) jen jednou – i když ji outbox doručí opakovaně.
/// </summary>
public sealed class IdempotentHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDatabase _database = new();
    private readonly Switch _switch = new();
    private readonly ServiceProvider _services;

    private sealed class Switch
    {
        public bool FailAfterSave { get; set; }
        public int Calls { get; set; }
    }

    /// <summary>Handler, který přičte 1 ks na sklad, uloží – a na povel pak selže (změna už je v DB, ale nepotvrzená).</summary>
    private sealed class AddOneToStockHandler(MiniShopDbContext db, Switch @switch) : IDomainEventHandler<OrderCancelled>
    {
        public async Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken)
        {
            @switch.Calls++;
            (await db.Products.SingleAsync(cancellationToken)).AddStock(1);
            await db.SaveChangesAsync(cancellationToken);

            if (@switch.FailAfterSave)
            {
                throw new InvalidOperationException("Chyba po uložení");
            }
        }
    }

    /// <summary>Druhý handler téže události – ověří, že si každý handler odškrtává zvlášť.</summary>
    private sealed class CountingHandler(Switch @switch) : IDomainEventHandler<OrderCancelled>
    {
        public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken)
        {
            @switch.Calls++;
            return Task.CompletedTask;
        }
    }

    public IdempotentHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddInfrastructure(_database.ConnectionString);
        services.AddSingleton(_switch);

        _services = services.BuildServiceProvider();
        Db(db => db.Database.EnsureCreatedAsync()).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task StejnaUdalostDvakrat_HandlerProbehneJenJednou()
    {
        using var services = BuildWith(s => s.AddDomainEventHandler<OrderCancelled, AddOneToStockHandler>());
        var cancelled = await CreateCancelledEventAsync();

        await DispatchAsync(services, cancelled);
        await DispatchAsync(services, cancelled);   // opakované doručení – stejné EventId

        Assert.Equal(1, _switch.Calls);
        Assert.Equal(6, await StockAsync());   // 5 + 1, ne 5 + 2
        var processed = Assert.Single(await InboxOfAsync<AddOneToStockHandler>());
        Assert.Equal(cancelled.EventId, processed.EventId);
    }

    [Fact]
    public async Task JinaUdalost_HandlerProbehneZnovu()
    {
        using var services = BuildWith(s => s.AddDomainEventHandler<OrderCancelled, AddOneToStockHandler>());
        var cancelled = await CreateCancelledEventAsync();

        await DispatchAsync(services, cancelled);
        await DispatchAsync(services, cancelled with { EventId = Guid.NewGuid() });

        Assert.Equal(2, _switch.Calls);
        Assert.Equal(7, await StockAsync());
    }

    [Fact]
    public async Task ChybaHandleru_ZmenySeVratiANicSeNeodskrtne()
    {
        using var services = BuildWith(s => s.AddDomainEventHandler<OrderCancelled, AddOneToStockHandler>());
        var cancelled = await CreateCancelledEventAsync();
        _switch.FailAfterSave = true;

        await Assert.ThrowsAsync<AggregateException>(() => DispatchAsync(services, cancelled));

        Assert.Equal(5, await StockAsync());   // SaveChanges handleru proběhl, ale transakce se vrátila
        Assert.Empty(await InboxOfAsync<AddOneToStockHandler>());

        // Opakování po odstranění chyby projde a změnu uloží.
        _switch.FailAfterSave = false;
        await DispatchAsync(services, cancelled);
        Assert.Equal(6, await StockAsync());
    }

    [Fact]
    public async Task DvaHandleryTeheUdalosti_KazdySiOdskrtavaZvlast()
    {
        using var services = BuildWith(s =>
        {
            s.AddDomainEventHandler<OrderCancelled, AddOneToStockHandler>();
            s.AddDomainEventHandler<OrderCancelled, CountingHandler>();
        });
        var cancelled = await CreateCancelledEventAsync();

        await DispatchAsync(services, cancelled);
        await DispatchAsync(services, cancelled);

        Assert.Equal(2, _switch.Calls);   // každý handler jednou
        // 3 = OrderEventsLogHandler (z AddInfrastructure) + 2 testovací handlery
        Assert.Equal(3, (await Db(db => db.ProcessedMessages.ToListAsync())).Count);
    }

    private ServiceProvider BuildWith(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddInfrastructure(_database.ConnectionString);
        services.AddSingleton(_switch);
        register(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Doručí událost ve vlastním scope – jako jedno kolo OutboxProcessoru.</summary>
    private static async Task DispatchAsync(ServiceProvider services, OrderCancelled domainEvent)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchAsync([domainEvent]);
    }

    private Task<OrderCancelled> CreateCancelledEventAsync() => Db(async db =>
    {
        var product = new Product("Testovací produkt", null, 100m, 5);
        db.Products.Add(product);
        var order = new Order("Jan Novák", "jan@example.com", Now.UtcDateTime);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return new OrderCancelled(order, Now.UtcDateTime);
    });

    /// <summary>Záznamy inboxu jen pro daný handler (AddInfrastructure registruje i OrderEventsLogHandler).</summary>
    private async Task<List<ProcessedMessage>> InboxOfAsync<THandler>() =>
        (await Db(db => db.ProcessedMessages.ToListAsync()))
            .Where(m => m.Handler == typeof(THandler).FullName)
            .ToList();

    private Task<int> StockAsync() => Db(async db => (await db.Products.SingleAsync()).StockQuantity);

    private async Task<T> Db<T>(Func<MiniShopDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<MiniShopDbContext>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }
}
