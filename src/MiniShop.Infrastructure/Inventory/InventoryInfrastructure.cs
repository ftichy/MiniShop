using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Inventory;

/// <summary>Infrastruktura modulu Inventory (krok 10): databáze inventory.db, její outbox a inbox, repozitáře.</summary>
public static class InventoryInfrastructure
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<InventoryDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IInventoryUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());
        services.AddScoped<IInventoryMessagePublisher, InventoryMessagePublisher>();

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockReservationRepository, StockReservationRepository>();

        // Vlastní outbox = vlastní pošťák.
        services.AddSingleton<IOutboxProcessor, OutboxProcessor<InventoryDbContext>>();

        services.AddIntegrationModule<InventoryDbContext>(IntegrationModules.Inventory);

        return services;
    }
}
