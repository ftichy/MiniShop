using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Škálování krok 5: worker, který smí běžet jen na vedoucí kopii služby (<see cref="ILeaderElection"/>).
/// Po dobu každého vedení vytvoří <b>novou</b> instanci <typeparamref name="TWorker"/>, spustí ji a při ztrátě vedení
/// (nebo zastavení služby) ji zastaví. Získá-li kopie vedení znovu, worker běží od začátku – jednorázové kontroly po startu
/// (prázdná kopie katalogu…) tak zopakuje i nový vedoucí po pádu původního.
/// </summary>
/// <remarks>Registrace jen přes <see cref="LeaderElectionServiceCollectionExtensions.AddLeaderOnlyHostedService{TWorker}"/>.</remarks>
public sealed class LeaderOnlyHostedService<TWorker>(
    IServiceProvider services,
    ILeaderElection leaderElection,
    ILogger<LeaderOnlyHostedService<TWorker>> logger) : BackgroundService
    where TWorker : class, IHostedService
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CancellationToken term;
            try
            {
                term = await leaderElection.WaitForLeadershipAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunTermAsync(term, stoppingToken);
        }
    }

    private async Task RunTermAsync(CancellationToken term, CancellationToken stoppingToken)
    {
        using var termOrStopping = CancellationTokenSource.CreateLinkedTokenSource(term, stoppingToken);
        var worker = ActivatorUtilities.CreateInstance<TWorker>(services);
        try
        {
            await worker.StartAsync(termOrStopping.Token);
            try
            {
                // Worker běží, dokud kopie nepřijde o vedení nebo se služba nezastavuje.
                await Task.Delay(Timeout.Infinite, termOrStopping.Token);
            }
            catch (OperationCanceledException)
            {
            }

            using var stopTimeout = new CancellationTokenSource(StopTimeout);
            await worker.StopAsync(stopTimeout.Token);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "[Vedoucí] Worker {Worker} selhal při spuštění nebo zastavení", typeof(TWorker).Name);
        }
        finally
        {
            (worker as IDisposable)?.Dispose();
        }
    }
}

public static class LeaderElectionServiceCollectionExtensions
{
    /// <summary>
    /// Volba vedoucí kopie služby (škálování krok 5). <paramref name="lockName"/> = jméno služby (každá služba má vlastního vedoucího,
    /// protože má jiné workery). Musí být zaregistrovaná dřív než workery – host je pak zastaví v opačném pořadí
    /// (nejdřív workery, nakonec zámek).
    /// </summary>
    public static IServiceCollection AddLeaderElection(
        this IServiceCollection services, Persistence.ModuleDatabase database, string lockName)
    {
        services.AddSingleton(new LeaderElectionOptions(lockName));
        services.AddSingleton<LeaderElection>(provider => new LeaderElection(
            database, provider.GetRequiredService<LeaderElectionOptions>(),
            provider.GetRequiredService<ILogger<LeaderElection>>()));
        services.AddSingleton<ILeaderElection>(provider => provider.GetRequiredService<LeaderElection>());
        services.AddHostedService(provider => provider.GetRequiredService<LeaderElection>());
        return services;
    }

    /// <summary>
    /// Worker s časovačem, který má běžet jen jednou za celou službu (outbox, termíny ság, úklid…), ne v každé kopii.
    /// Consumery RabbitMQ sem nepatří – ty běží ve všech kopiích a zprávy si rozdělí fronta.
    /// </summary>
    public static IServiceCollection AddLeaderOnlyHostedService<TWorker>(this IServiceCollection services)
        where TWorker : class, IHostedService =>
        services.AddHostedService<LeaderOnlyHostedService<TWorker>>();
}
