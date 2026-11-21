using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Proud B1: po startu založí proud <c>minishop.events</c> a naváže ho na exchange všech kontraktů (idempotentně – nový
/// kontrakt se tak naváže s nasazením kódu). Jen vedoucí kopie Webu; RabbitMQ ještě neběží = zkusí to znovu za 10 s.
/// </summary>
public sealed class EventStreamSetupWorker(RabbitMqConnection connection, ILogger<EventStreamSetupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RabbitMqEventStream.SetupAsync(connection, stoppingToken);
                logger.LogInformation("Proud zpráv {Stream} připraven (retence {MaxAge})", RabbitMqEventStream.StreamName, RabbitMqEventStream.MaxAge);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Proud zpráv {Stream} se nepodařilo založit: {Error}. Další pokus za {Delay} s.",
                    RabbitMqEventStream.StreamName, ex.Message, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}

public static class EventStreamServiceCollectionExtensions
{
    /// <summary>Proud B1: proud zpráv – založení (vedoucí kopie) a čtení. Patří za <c>AddMessaging</c> (potřebuje RabbitMQ).</summary>
    public static IServiceCollection AddEventStream(this IServiceCollection services)
    {
        services.AddSingleton<IEventStreamReader, RabbitMqEventStreamReader>();
        services.AddLeaderOnlyHostedService<EventStreamSetupWorker>();
        return services;
    }
}
