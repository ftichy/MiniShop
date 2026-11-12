using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Notifications;

using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Notifications;

/// <summary>
/// E-shop krok 7: infrastruktura modulu Notifications – notifications.db (příjemci + inbox), repozitář.
/// Bez outboxu a pošťáka: modul nic neposílá. Vlastní fronta minishop.notifications v RabbitMQ.
/// </summary>
public static class NotificationsInfrastructure
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services, ModuleDatabase database)
    {
        services.AddDbContext<NotificationsDbContext>(options => options.UseModuleDatabase(database, NotificationsDbContext.Schema));
        services.AddScoped<INotificationsUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());
        services.AddScoped<IOrderRecipientRepository, OrderRecipientRepository>();
        services.AddScoped<IBackInStockRepository, BackInStockRepository>();

        services.AddIntegrationModule<NotificationsDbContext>(IntegrationModules.Notifications);

        return services;
    }
}
