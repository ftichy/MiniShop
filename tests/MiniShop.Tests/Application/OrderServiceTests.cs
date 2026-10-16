using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Contracts;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Pricing;
using MiniShop.Application.Services;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Products;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Repositories;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Application;

/// <summary>
/// Testy OrderService nad skutečnou SQLite databází v paměti.
/// </summary>
public sealed class OrderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly MiniShopDbContext _db;
    private readonly RecordingOrderNotifier _notifier = new();
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MiniShopDbContext>().UseSqlite(_connection).Options;
        _db = new MiniShopDbContext(options);
        _db.Database.EnsureCreated();

        _service = new OrderService(
            new OrderRepository(_db),
            new ProductRepository(_db),
            _db,
            [new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m)],
            _notifier,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task PlaceOrder_OdecteZasobuAUlozObjednavku()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await _service.PlaceOrderAsync(CreateRequest((product.Id, 3)));

        Assert.True(order.Id > 0);
        Assert.Equal(300m, order.TotalPrice);
        Assert.Equal(Now.UtcDateTime, order.CreatedAtUtc);
        Assert.Equal(2, (await _db.Products.SingleAsync()).StockQuantity);
        Assert.Contains($"placed:{order.Id}", _notifier.Events);
    }

    [Fact]
    public async Task PlaceOrder_Od10Kusu_UplatniMnozstevniSlevu()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);

        var order = await _service.PlaceOrderAsync(CreateRequest((product.Id, 10)));

        Assert.Equal(100m, order.Items.Single().Discount);
        Assert.Equal(900m, order.TotalPrice);
    }

    [Fact]
    public async Task PlaceOrder_StejnyProduktVicekrat_SeSlouciDoJednePolozky()
    {
        var product = await AddProductAsync(price: 100m, stock: 50);

        var order = await _service.PlaceOrderAsync(CreateRequest((product.Id, 4), (product.Id, 6)));

        var item = Assert.Single(order.Items);
        Assert.Equal(10, item.Quantity);
    }

    [Fact]
    public async Task PlaceOrder_NedostatekZbozi_NicSeNeulozi()
    {
        var product = await AddProductAsync(price: 100m, stock: 2);

        await Assert.ThrowsAsync<InsufficientStockException>(
            () => _service.PlaceOrderAsync(CreateRequest((product.Id, 3))));

        Assert.Empty(await _db.Orders.ToListAsync());
        Assert.Empty(_notifier.Events);
    }

    [Fact]
    public async Task PlaceOrder_NeexistujiciProdukt_VyhodiNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.PlaceOrderAsync(CreateRequest((999, 1))));
    }

    [Fact]
    public async Task Cancel_VratiZboziNaSklad()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await _service.PlaceOrderAsync(CreateRequest((product.Id, 5)));

        var cancelled = await _service.CancelAsync(order.Id);

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(5, (await _db.Products.SingleAsync()).StockQuantity);
        Assert.Contains($"cancelled:{order.Id}", _notifier.Events);
    }

    [Fact]
    public async Task Cancel_VyrizenouObjednavku_VyhodiDomainException()
    {
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await _service.PlaceOrderAsync(CreateRequest((product.Id, 1)));
        await _service.CompleteAsync(order.Id);

        await Assert.ThrowsAsync<DomainException>(() => _service.CancelAsync(order.Id));
    }

    private async Task<Product> AddProductAsync(decimal price, int stock)
    {
        var product = new Product("Testovací produkt", null, price, stock);
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
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
        _db.Dispose();
        _connection.Dispose();
    }
}
