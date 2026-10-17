using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application.Abstractions;
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
        services.AddScoped<IOrderNotifier, LoggingOrderNotifier>();

        // Doménové události (krok 2): dispatcher + handlery.
        // Scoped – handlery sdílejí DbContext se scope, ve kterém OutboxProcessor zprávu zpracovává.
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddDomainEventHandler<OrderPlaced, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCompleted, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCancelled, OrderEventsLogHandler>();

        // Outbox (kroky 4 a 5): procesor je singleton a pro každou zprávu si otevře vlastní scope.
        // Čas pro ProcessedAtUtc; obvykle ho už zaregistrovala aplikační vrstva.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OutboxEventReader>();
        services.AddSingleton<OutboxProcessor>();

        // Integrační události (krok 7): publikování = zápis do outboxu, odeslání ven = bus.
        // Výchozí bus jen loguje; AddRabbitMq (krok 8) ho nahradí RabbitMQ.
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventBus, LoggingIntegrationEventBus>();

        return services;
    }

    /// <summary>
    /// Krok 8: integrační události odcházejí do RabbitMQ místo do logu. Přidá i ukázkového consumera
    /// (<see cref="RabbitMqAuditConsumer"/>) – spouští ho IntegrationEventAuditWorker ve Webu.
    /// </summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, RabbitMqOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<RabbitMqConnection>();
        services.Replace(ServiceDescriptor.Singleton<IIntegrationEventBus, RabbitMqIntegrationEventBus>());
        services.AddSingleton<RabbitMqAuditConsumer>();

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
