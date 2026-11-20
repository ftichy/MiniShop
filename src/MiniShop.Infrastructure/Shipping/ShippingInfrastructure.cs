using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Shipping;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Shipping;

/// <summary>
/// E-shop krok 26B: infrastruktura modulu Shipping – schéma shipping (zásilky, outbox, inbox), repozitář.
/// Klienta dopravce (<see cref="CarrierApiClient"/>) registruje Web – potřebuje adresu a klíč ze ServiceDefaults.
/// </summary>
public static class ShippingInfrastructure
{
    /// <param name="webhookSecret">E-shop krok 26C: tajemství podpisu webhooků dopravce (min. 32 znaků, jinak se služba nespustí).</param>
    public static IServiceCollection AddShippingInfrastructure(this IServiceCollection services, ModuleDatabase database,
        string? webhookSecret)
    {
        services.AddDbContext<ShippingDbContext>(options => options.UseModuleDatabase(database, ShippingDbContext.Schema));
        services.AddScoped<IShippingUnitOfWork>(sp => sp.GetRequiredService<ShippingDbContext>());
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<ShipmentTrackingService>();   // e-shop krok 26C: webhook + dohledání
        services.AddScoped<ShipmentQueryService>();
        services.AddSingleton(new CarrierWebhookSettings(webhookSecret));
        services.AddScoped<CarrierWebhookProcessor>();
        services.AddSingleton<IOperationalMetricsSource, ShipmentStatusSource>();   // e-shop krok 26E: zásilky pro Grafanu

        services.AddSingleton<IOutboxProcessor, OutboxProcessor<ShippingDbContext>>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck<ShippingDbContext>>("outbox-shipping");

        services.AddIntegrationModule<ShippingDbContext>(IntegrationModules.Shipping);
        return services;
    }
}

/// <summary>E-shop krok 26B: vytvoří nebo aktualizuje schéma shipping (EF migrace).</summary>
public static class ShippingDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<ShippingDbContext>(), cancellationToken);
    }

    public static Task MigrateAsync(ShippingDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, cancellationToken);
}
