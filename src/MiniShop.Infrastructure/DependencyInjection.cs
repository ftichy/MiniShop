using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Events.Handlers;
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
        services.AddScoped<IDomainEventHandler<OrderPlaced>, OrderEventsLogHandler>();
        services.AddScoped<IDomainEventHandler<OrderCompleted>, OrderEventsLogHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelled>, OrderEventsLogHandler>();

        // Outbox (kroky 4 a 5): procesor je singleton a pro každou zprávu si otevře vlastní scope.
        // Čas pro ProcessedAtUtc; obvykle ho už zaregistrovala aplikační vrstva.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OutboxEventReader>();
        services.AddSingleton<OutboxProcessor>();

        return services;
    }
}
