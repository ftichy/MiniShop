using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Reporting;
using MiniShop.Contracts.Reporting;
using MiniShop.Domain.Reporting;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 27A: dávka nad přehledem tržeb (PostgreSQL) – jen objednávky, které si zákazník nechal; zpráva
/// <see cref="ProductRecommendationsChangedV1"/> jen při změně; zmizelé doporučení = prázdný seznam.
/// </summary>
public sealed class ProductRecommendationServiceTests : IDisposable
{
    private readonly TestDatabases _databases = new();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _services;

    public ProductRecommendationServiceTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(_time);
        services.AddInfrastructureCore();
        services.AddReportingInfrastructure(_databases.Reporting);
        services.AddScoped<ProductRecommendationService>();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _databases.Dispose();
    }

    [Fact]
    public async Task Prepocet_PosilaJenZmeny_BezZrusenychAVracenych()
    {
        await AddOrderAsync(1, SalesOrderStatus.Completed, 10, 20);
        await AddOrderAsync(2, SalesOrderStatus.Confirmed, 10, 20);
        await AddOrderAsync(3, SalesOrderStatus.Cancelled, 10, 30);   // nepočítá se
        await AddOrderAsync(4, SalesOrderStatus.Returned, 10, 30);    // nepočítá se

        Assert.Equal(2, await RecomputeAsync());   // 10 → [20], 20 → [10]
        var first = await PublishedAsync();
        Assert.Equal([20], first.Single(m => m.ProductId == 10).RecommendedProductIds);
        Assert.Equal([10], first.Single(m => m.ProductId == 20).RecommendedProductIds);

        // Stejná data = žádná zpráva (jinak by každých 5 minut odešla zpráva za každý produkt).
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await RecomputeAsync());
        Assert.Equal(2, (await PublishedAsync()).Count);
    }

    [Fact]
    public async Task Prepocet_ZmizeleDoporuceni_PosleZPrazdnymSeznamem()
    {
        await AddOrderAsync(1, SalesOrderStatus.Confirmed, 10, 20);
        await AddOrderAsync(2, SalesOrderStatus.Confirmed, 10, 20);
        await RecomputeAsync();

        // Zákazník objednávku zrušil – dvojice už je jen v jedné objednávce.
        await using (var scope = _services.CreateAsyncScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<ISalesOrderRepository>();
            (await orders.GetAsync(2))!.ApplyStatus(SalesOrderStatus.Cancelled, UtcNow);
            await scope.ServiceProvider.GetRequiredService<IReportingUnitOfWork>().SaveChangesAsync();
        }

        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(2, await RecomputeAsync());
        var cleared = (await PublishedAsync()).Where(m => m.ComputedAtUtc == UtcNow).ToList();
        Assert.Equal(2, cleared.Count);
        Assert.All(cleared, m => Assert.Empty(m.RecommendedProductIds));
    }

    [Fact]
    public async Task Prepocet_PrazdnyPrehled_NicNemaze()
    {
        // Přestavění přehledu tržeb (smazané objednávky) nesmí rozeslat „smazat doporučení“ všem produktům.
        Assert.Equal(0, await RecomputeAsync());
        Assert.Empty(await PublishedAsync());
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private async Task AddOrderAsync(int orderId, SalesOrderStatus status, params int[] productIds)
    {
        await using var scope = _services.CreateAsyncScope();
        var order = SalesOrder.Create(orderId, UtcNow);
        order.ApplyPlaced(UtcNow.AddHours(-1), 100m, 0m,
            productIds.Select(id => new SalesOrderLine(id, $"Produkt {id}", 1, 50m)), UtcNow);
        order.ApplyStatus(status, UtcNow);
        scope.ServiceProvider.GetRequiredService<ISalesOrderRepository>().Add(order);
        await scope.ServiceProvider.GetRequiredService<IReportingUnitOfWork>().SaveChangesAsync();
    }

    private async Task<int> RecomputeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProductRecommendationService>().RecomputeAsync();
    }

    private async Task<List<ProductRecommendationsChangedV1>> PublishedAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().OutboxMessages
            .Where(m => m.Type == nameof(ProductRecommendationsChangedV1))
            .ToListAsync();
        return messages.Select(m => JsonSerializer.Deserialize<ProductRecommendationsChangedV1>(m.Payload)!).ToList();
    }
}
