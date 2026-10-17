using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure.Events;
using MiniShop.Infrastructure.Events.Handlers;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Repositories;

namespace MiniShop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // DbContext potřebuje čas pro ProcessedAtUtc v outboxu (krok 4). Obvykle ho už zaregistrovala aplikační vrstva.
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<MiniShopDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MiniShopDbContext>());

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderNotifier, LoggingOrderNotifier>();

        // Doménové události (krok 2): dispatcher + handlery.
        // Scoped – handlery tak sdílejí stejný DbContext jako požadavek, který událost vyvolal.
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventHandler<OrderPlaced>, OrderEventsLogHandler>();
        services.AddScoped<IDomainEventHandler<OrderCompleted>, OrderEventsLogHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelled>, OrderEventsLogHandler>();

        return services;
    }
}
