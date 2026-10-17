using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Krok 4 EDA: události se ukládají do outboxu ve stejné transakci jako data
/// a po doručení se označí jako zpracované – nebo zůstanou nezpracované s chybou.
/// </summary>
public sealed class OutboxTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly SwitchableDispatcher _dispatcher = new();
    private readonly MiniShopDbContext _db;

    /// <summary>Dispatcher, který na povel selže – simuluje chybu handleru.</summary>
    private sealed class SwitchableDispatcher : IDomainEventDispatcher
    {
        public bool Fail { get; set; }

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("Sklad nedostupný") : Task.CompletedTask;
    }

    public OutboxTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MiniShopDbContext>().UseSqlite(_connection).Options;
        _db = new MiniShopDbContext(options, _dispatcher, new FixedTimeProvider(Now));
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Ulozeni_ZapiseUdalostDoOutboxuSDatyObjednavky()
    {
        var order = await PlaceOrderAsync(quantity: 3);

        var message = await _db.OutboxMessages.SingleAsync();
        Assert.Equal("OrderPlaced", message.Type);
        Assert.Equal(Now.UtcDateTime, message.OccurredAtUtc);

        // Payload jsou jen data, ne entita – a objednávka v nich už má ID.
        var payload = JsonSerializer.Deserialize<OrderPayload>(message.Payload)!;
        Assert.Equal(order.Id, payload.OrderId);
        Assert.Equal("jan@example.com", payload.CustomerEmail);
        var line = Assert.Single(payload.Lines);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public async Task UspesneDoruceni_OznaciZpravuJakoZpracovanou()
    {
        await PlaceOrderAsync(quantity: 1);

        var message = await _db.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(Now.UtcDateTime, message.ProcessedAtUtc);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task ChybaHandleru_ObjednavkaZustaneUlozenaAZpravaNezpracovanaSChybou()
    {
        var order = await PlaceOrderAsync(quantity: 1);
        _dispatcher.Fail = true;

        order.Cancel(Now.UtcDateTime);
        await _db.SaveChangesAsync();   // chyba handleru se ven nepropaguje – data jsou uložená

        Assert.Equal(OrderStatus.Cancelled, (await _db.Orders.AsNoTracking().SingleAsync()).Status);

        var cancelled = await _db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Type == "OrderCancelled");
        Assert.Null(cancelled.ProcessedAtUtc);   // čeká na opakování (krok 5)
        Assert.Contains("Sklad nedostupný", cancelled.Error);
    }

    [Fact]
    public async Task UlozeniBezUdalosti_OutboxNemeni()
    {
        _db.Products.Add(new Product("Kabel", null, 100m, 5));
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.OutboxMessages.ToListAsync());
    }

    private async Task<Order> PlaceOrderAsync(int quantity)
    {
        var product = new Product("Testovací produkt", null, 100m, 10);
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var order = new Order("Jan Novák", "jan@example.com", Now.UtcDateTime);
        order.AddItem(product, quantity, discount: 0m);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
