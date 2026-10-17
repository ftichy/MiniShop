using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Hosting zpráv pro službu (krok 10B): outbox worker + RabbitMQ s consumery. Používá ho Web i Inventory.Api.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Doručování z outboxu na pozadí (krok 5) a integrační zprávy přes RabbitMQ (kroky 8 a 9).
    /// </summary>
    /// <remarks>
    /// Krok 10B: služby běží ve dvou procesech, takže zprávy mezi nimi musí jít přes broker.
    /// Transport "InProcess" (doručení v paměti) zůstává jen pro testy, kde jsou oba moduly v jednom procesu.
    /// Adresu brokeru předává Aspire jako connection string "messaging" (amqp://…); bez Aspire se použije
    /// sekce Messaging:RabbitMq (HostName, Port, …).
    /// </remarks>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var transport = configuration["Messaging:Transport"] ?? "RabbitMq";
        if (!string.Equals(transport, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Messaging:Transport '{transport}' není podporovaný. Od kroku 10B běží Ordering a Inventory ve dvou procesech " +
                "a zprávy si předávají jen přes RabbitMQ (InProcess funguje jen v testech).");
        }

        var options = configuration.GetSection("Messaging:RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        options.ConnectionString ??= configuration.GetConnectionString("messaging");

        services.AddRabbitMq(options);
        services.AddHostedService<RabbitMqConsumersWorker>();
        services.AddHostedService<OutboxWorker>();

        return services;
    }
}
