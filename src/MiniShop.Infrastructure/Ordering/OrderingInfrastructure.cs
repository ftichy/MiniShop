using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Inbox;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>Infrastruktura modulu Ordering (krok 10): databáze ordering.db, její outbox a inbox, repozitáře.</summary>
public static class OrderingInfrastructure
{
    public static IServiceCollection AddOrderingInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OrderingDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IOrderingUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());
        services.AddScoped<IOrderingMessagePublisher, OrderingMessagePublisher>();

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderSagaRepository, OrderSagaRepository>();
        services.AddScoped<ICatalogProductRepository, CatalogProductRepository>();
        services.AddScoped<IOrderNotifier, LoggingOrderNotifier>();

        // Doménové události (kroky 2–6) – má je jen Ordering, putují přes jeho outbox.
        // Scoped – handlery sdílejí DbContext se scope, ve kterém OutboxProcessor zprávu zpracovává.
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventReader, OrderingDomainEventReader>();
        services.AddDomainEventHandler<OrderPlaced, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderConfirmed, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderRejected, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCompleted, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCancelled, OrderEventsLogHandler>();

        // Outbox modulu (kroky 4 a 5): pošťák je singleton a pro každou zprávu si otevře vlastní scope.
        services.AddSingleton<IOutboxProcessor, OutboxProcessor<OrderingDbContext>>();

        // Krok 11: /health hlásí Degraded, když v outboxu visí zpráva déle než 5 minut.
        services.AddHealthChecks().AddCheck<OutboxHealthCheck<OrderingDbContext>>("outbox-ordering");

        services.AddIntegrationModule<OrderingDbContext>(IntegrationModules.Ordering);

        return services;
    }

    /// <summary>
    /// Zaregistruje handler doménové události VŽDY obalený dekorátorem <see cref="IdempotentDomainEventHandler{TEvent}"/>
    /// (krok 6). Handlery registrujte jen touto metodou – jinak by při opakovaném doručení proběhly víckrát.
    /// Inbox leží v ordering.db – doménové události posílá přes outbox jen modul Ordering.
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
            new InboxGuard(sp.GetRequiredService<OrderingDbContext>(), sp.GetRequiredService<TimeProvider>())));

        return services;
    }
}
