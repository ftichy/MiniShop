using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Vzhled krok F3a: spustí rozesílací consumer (<see cref="RabbitMqBroadcastConsumer"/>) – v <b>každé</b> kopii služby
/// (jako ostatní consumery), ne jen ve vedoucí: každá kopie má své připojené prohlížeče.
/// </summary>
public sealed class RabbitMqBroadcastWorker(IServiceProvider services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();   // nebrzdit start aplikace
        await ActivatorUtilities.CreateInstance<RabbitMqBroadcastConsumer>(services).RunAsync(stoppingToken);
    }
}

public static class BroadcastServiceCollectionExtensions
{
    /// <summary>
    /// Vzhled krok F3a: zprávy daných kontraktů rozeslat každé kopii služby a předat <typeparamref name="THandler"/>
    /// (singleton – běží mimo požadavek). Patří za <c>AddMessaging</c> (potřebuje RabbitMQ).
    /// </summary>
    public static IServiceCollection AddBroadcastConsumer<THandler>(this IServiceCollection services, params Type[] contracts)
        where THandler : class, IBroadcastMessageHandler
    {
        if (contracts.Any(c => !typeof(IIntegrationMessage).IsAssignableFrom(c)))
        {
            throw new ArgumentException("Rozesílat jde jen integrační zprávy (kontrakty z MiniShop.Contracts).", nameof(contracts));
        }

        services.AddSingleton(new BroadcastSubscription(contracts.Select(c => c.Name).ToList()));
        services.AddSingleton<IBroadcastMessageHandler, THandler>();
        services.AddHostedService<RabbitMqBroadcastWorker>();
        return services;
    }
}
