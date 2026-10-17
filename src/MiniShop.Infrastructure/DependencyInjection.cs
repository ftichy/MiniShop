using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure;

/// <summary>
/// Registrace infrastruktury. Společné věci (bus, směrování zpráv) jsou tady, moduly mají vlastní metody
/// (<see cref="OrderingInfrastructure.AddOrderingInfrastructure"/>, <see cref="InventoryInfrastructure.AddInventoryInfrastructure"/>).
/// </summary>
public static class DependencyInjection
{
    /// <param name="orderingConnectionString">Databáze modulu Ordering (ordering.db).</param>
    /// <param name="inventoryConnectionString">Databáze modulu Inventory (inventory.db).</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string orderingConnectionString, string inventoryConnectionString)
    {
        // Čas pro ProcessedAtUtc apod.; obvykle ho už zaregistrovala aplikační vrstva.
        services.TryAddSingleton(TimeProvider.System);

        // Integrační zprávy (kroky 7 a 9): publikování = zápis do outboxu modulu, doručení = bus.
        // Výchozí bus doručuje handlerům modulů přímo v procesu; AddRabbitMq (krok 8) ho nahradí RabbitMQ.
        services.AddSingleton<IIntegrationBus, InProcessIntegrationBus>();
        services.AddSingleton<IntegrationMessageRouting>();
        services.AddScoped<IntegrationMessageDispatcher>();

        // Moduly (krok 10) – každý s vlastní databází, outboxem a inboxem.
        services.AddOrderingInfrastructure(orderingConnectionString);
        services.AddInventoryInfrastructure(inventoryConnectionString);

        return services;
    }

    /// <summary>
    /// Krok 8: integrační zprávy odcházejí do RabbitMQ. Přidá i consumery – auditní frontu a frontu pro každý modul
    /// (krok 9); spouští je RabbitMqConsumersWorker ve Webu.
    /// </summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, RabbitMqOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<RabbitMqConnection>();
        services.Replace(ServiceDescriptor.Singleton<IIntegrationBus, RabbitMqIntegrationBus>());
        services.AddSingleton<RabbitMqAuditConsumer>();
        services.AddSingleton<RabbitMqConsumers>();

        return services;
    }

    /// <summary>
    /// Modul (krok 10): jméno (= fronta "minishop.{modul}" v RabbitMQ) a jeho databáze, ve které leží inbox handlerů.
    /// </summary>
    public static IServiceCollection AddIntegrationModule<TDbContext>(this IServiceCollection services, string module)
        where TDbContext : ModuleDbContext
    {
        services.AddSingleton(new IntegrationModule(module, typeof(TDbContext)));
        return services;
    }

    /// <summary>
    /// Zaregistruje handler integrační zprávy (krok 9) pro daný modul ("ordering", "inventory").
    /// Modul = vlastní fronta v RabbitMQ. Handler vždy poběží přes inbox v databázi modulu.
    /// </summary>
    public static IServiceCollection AddIntegrationMessageHandler<TMessage, THandler>(this IServiceCollection services, string module)
        where TMessage : IIntegrationMessage
        where THandler : class, IIntegrationMessageHandler<TMessage>
    {
        services.TryAddScoped<THandler>();
        services.AddSingleton(new IntegrationMessageSubscription(module, typeof(TMessage), typeof(THandler),
            (sp, message, cancellationToken) => sp.GetRequiredService<THandler>().HandleAsync((TMessage)message, cancellationToken)));

        return services;
    }
}
