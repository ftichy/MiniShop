using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Web.BackgroundServices;

namespace MiniShop.Web.Extensions;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Jak se doručují integrační zprávy (kroky 8 a 9) – podle Messaging:Transport v appsettings.json:
    /// "RabbitMq" = přes broker (fronta pro každý modul + auditní fronta),
    /// "InProcess" = přímo handlerům v procesu (sága funguje i bez Dockeru).
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var transport = configuration["Messaging:Transport"] ?? "InProcess";

        if (string.Equals(transport, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            var options = configuration.GetSection("Messaging:RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
            services.AddRabbitMq(options);
            services.AddHostedService<RabbitMqConsumersWorker>();
        }
        else if (!string.Equals(transport, "InProcess", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Neznámý Messaging:Transport '{transport}'. Povolené hodnoty: RabbitMq, InProcess.");
        }

        return services;
    }
}
