using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Ordering;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Krok 9: kdo co odebírá. Podle toho vznikají fronty modulů v RabbitMQ a InProcessIntegrationBus vybírá handlery.
/// </summary>
public sealed class IntegrationMessageRoutingTests
{
    private sealed class Handler : IIntegrationMessageHandler<ReserveStockV1>, IIntegrationMessageHandler<StockReservedV1>
    {
        public Task HandleAsync(ReserveStockV1 message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HandleAsync(StockReservedV1 message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OtherHandler : IIntegrationMessageHandler<ReserveStockV1>, IIntegrationMessageHandler<StockReservedV1>
    {
        public Task HandleAsync(ReserveStockV1 message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HandleAsync(StockReservedV1 message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Udalost_MuzeMitVicPrijemcuVRuznychModulech()
    {
        var routing = Routing(services => services
            .AddIntegrationMessageHandler<StockReservedV1, Handler>("ordering")
            .AddIntegrationMessageHandler<StockReservedV1, OtherHandler>("reporting"));

        Assert.Equal(new[] { "ordering", "reporting" }, routing.ModulesFor(typeof(StockReservedV1)));
    }

    [Fact]
    public void Prikaz_SmiMitJenJednohoPrijemce()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Routing(services => services
            .AddIntegrationMessageHandler<ReserveStockV1, Handler>("inventory")
            .AddIntegrationMessageHandler<ReserveStockV1, OtherHandler>("warehouse")));

        Assert.Contains("ReserveStockV1", ex.Message);
    }

    [Fact]
    public void Modul_OdebiraJenSveKontrakty()
    {
        var routing = Routing(services => services
            .AddIntegrationMessageHandler<ReserveStockV1, Handler>("inventory")
            .AddIntegrationMessageHandler<StockReservedV1, Handler>("ordering"));

        Assert.Equal(new[] { "inventory", "ordering" }, routing.Modules);
        Assert.Equal(new[] { "ReserveStockV1" }, routing.ContractNamesFor("inventory"));
        Assert.Empty(routing.ModulesFor(typeof(StockReleasedV1)));
    }

    [Fact]
    public void ModulBezDatabaze_AplikaceNenastartuje()
    {
        var services = new ServiceCollection();
        services.AddIntegrationMessageHandler<StockReservedV1, Handler>("ordering");
        services.AddSingleton<IntegrationMessageRouting>();
        using var provider = services.BuildServiceProvider();

        // Krok 10: modul musí říct, ve které databázi má inbox.
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IntegrationMessageRouting>());
        Assert.Contains("ordering", ex.Message);
    }

    private static IntegrationMessageRouting Routing(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        foreach (var module in new[] { "ordering", "inventory", "reporting", "warehouse" })
        {
            services.AddIntegrationModule<OrderingDbContext>(module);   // na typu databáze tu nezáleží
        }

        services.AddSingleton<IntegrationMessageRouting>();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IntegrationMessageRouting>();
    }
}
