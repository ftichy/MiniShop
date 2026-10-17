using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Web.BackgroundServices;

namespace MiniShop.Web.Extensions;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Kam odcházejí integrační události (krok 8) – podle Messaging:Transport v appsettings.json:
    /// "RabbitMq" = do brokeru + ukázkový consumer, "Log" = jen do logu (aplikace běží i bez Dockeru).
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var transport = configuration["Messaging:Transport"] ?? "Log";

        if (string.Equals(transport, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            var options = configuration.GetSection("Messaging:RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
            services.AddRabbitMq(options);
            services.AddHostedService<IntegrationEventAuditWorker>();
        }
        else if (!string.Equals(transport, "Log", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Neznámý Messaging:Transport '{transport}'. Povolené hodnoty: RabbitMq, Log.");
        }

        return services;
    }
}
