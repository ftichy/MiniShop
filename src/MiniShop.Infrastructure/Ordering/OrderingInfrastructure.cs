using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Application.Ordering.Coupons;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Inbox;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;

using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>Infrastruktura modulu Ordering (krok 10): databáze ordering.db, její outbox a inbox, repozitáře.</summary>
public static class OrderingInfrastructure
{
    public static IServiceCollection AddOrderingInfrastructure(this IServiceCollection services, ModuleDatabase database)
    {
        services.AddDbContext<OrderingDbContext>(options => options.UseModuleDatabase(database, OrderingDbContext.Schema));
        services.AddScoped<IOrderingUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());
        services.AddScoped<IOrderingMessagePublisher, OrderingMessagePublisher>();

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderSagaRepository, OrderSagaRepository>();
        services.AddScoped<IReturnSagaRepository, ReturnSagaRepository>();   // e-shop krok 12
        services.AddScoped<ICouponRepository, CouponRepository>();             // e-shop krok 13
        services.AddScoped<ICatalogProductRepository, CatalogProductRepository>();
        services.AddScoped<ICartRepository, CartRepository>();   // e-shop krok 3
        services.AddScoped<ICartEventStore, CartEventStore>();   // e-shop krok 17: čtení událostí košíků

        // Doménové události (kroky 2–6) – má je jen Ordering, putují přes jeho outbox.
        // Scoped – handlery sdílejí DbContext se scope, ve kterém OutboxProcessor zprávu zpracovává.
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventReader, OrderingDomainEventReader>();
        services.AddDomainEventHandler<OrderPlaced, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderConfirmed, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderRejected, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCompleted, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderCancelled, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderPackingStarted, OrderEventsLogHandler>();   // e-shop krok 6
        services.AddDomainEventHandler<OrderShipped, OrderEventsLogHandler>();
        services.AddDomainEventHandler<OrderAwaitingPayment, OrderEventsLogHandler>();   // e-shop krok 8

        // Outbox modulu (kroky 4 a 5): pošťák je singleton a pro každou zprávu si otevře vlastní scope.
        services.AddSingleton<IOutboxProcessor, OutboxProcessor<OrderingDbContext>>();

        // Krok 11: /health hlásí Degraded, když v outboxu visí zpráva déle než 5 minut.
        services.AddHealthChecks().AddCheck<OutboxHealthCheck<OrderingDbContext>>("outbox-ordering");
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<OrderingDbContext>>("database-ordering");   // škálování krok 3: databáze odpovídá

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
