using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Events.Handlers;
using MiniShop.Infrastructure.Inbox;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Repositories;

namespace MiniShop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MiniShopDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MiniShopDbContext>());

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderSagaRepository, OrderSagaRepository>();
        services.AddScoped<IStockReservationRepository, StockReservationRepository>();
        services.AddScoped<IOrderNotifier, LoggingOrderNotifier>();

        // Doménové události (krok 2): dispatcher + handlery.
        // Scoped – handlery sdílejí DbContext se scope, ve kterém OutboxProcessor zprávu zpracovává.
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddDomainEventHandler<OrderPlaced, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderConfirmed, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderRejected, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCompleted, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCancelled, OrderEventsLogHandler>();

        // Outbox (kroky 4 a 5): procesor je singleton a pro každou zprávu si otevře vlastní scope.
        // Čas pro ProcessedAtUtc; obvykle ho už zaregistrovala aplikační vrstva.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OutboxEventReader>();
        services.AddSingleton<OutboxProcessor>();

        // Integrační zprávy (kroky 7 a 9): publikování = zápis do outboxu, doručení = bus.
        // Výchozí bus doručuje handlerům modulů přímo v procesu; AddRabbitMq (krok 8) ho nahradí RabbitMQ.
        services.AddScoped<IIntegrationMessagePublisher, OutboxIntegrationMessagePublisher>();
        services.AddSingleton<IIntegrationBus, InProcessIntegrationBus>();
        services.AddSingleton<IntegrationMessageRouting>();
        services.AddScoped<IntegrationMessageDispatcher>();
        services.AddScoped<InboxGuard>();

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
    /// Zaregistruje handler integrační zprávy (krok 9) pro daný modul ("ordering", "inventory").
    /// Modul = vlastní fronta v RabbitMQ. Handler vždy poběží přes inbox (<see cref="InboxGuard"/>).
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

    /// <summary>
    /// Zaregistruje handler doménové události VŽDY obalený dekorátorem <see cref="IdempotentDomainEventHandler{TEvent}"/>
    /// (krok 6). Handlery registrujte jen touto metodou – jinak by při opakovaném doručení proběhly víckrát.
    /// </summary>
    public static IServiceCollection AddDomainEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        // Samotný handler (TryAdd: jedna třída může obsluhovat víc událostí, viz OrderEventsLogHandler).
        services.TryAddScoped<THandler>();

        // Pod rozhraním, které hledá dispatcher, je jen obal.
        services.AddScoped<IDomainEventHandler<TEvent>>(sp => new IdempotentDomainEventHandler<TEvent>(
            sp.GetRequiredService<THandler>(),
            sp.GetRequiredService<MiniShopDbContext>(),
            sp.GetRequiredService<TimeProvider>()));

        return services;
    }
}
