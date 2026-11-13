using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Observability;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Observabilita G2: každých 30 s zjistí stav (outbox, fronty, ságy) ze všech <see cref="IOperationalMetricsSource"/>
/// a uloží ho do <see cref="OperationalMetrics"/>. Běží <b>jen na vedoucí kopii</b> (<c>AddLeaderOnlyHostedService</c>) –
/// stav je společný pro všechny kopie, hlásit ho má jen jedna. Při ztrátě vedení snímek smaže (hlásí nová vedoucí).
/// </summary>
/// <remarks>
/// Selhání jednoho zdroje (RabbitMQ nedostupný…) nevadí ostatním – jeho hodnoty v tomto kole chybí a do logu jde varování.
/// </remarks>
public sealed class OperationalMetricsWorker(
    IEnumerable<IOperationalMetricsSource> sources,
    OperationalMetrics metrics,
    ILogger<OperationalMetricsWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await CollectAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            metrics.Clear();
        }
    }

    /// <summary>Jedno kolo měření (veřejné kvůli testům).</summary>
    public async Task CollectAsync(CancellationToken cancellationToken)
    {
        var readings = new List<OperationalReading>();
        foreach (var source in sources)
        {
            try
            {
                readings.AddRange(await source.ReadAsync(cancellationToken));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("[Metriky] Stav {Source} se nepodařilo zjistit, zkusí se za {Interval} s: {Error}",
                    source.Name, Interval.TotalSeconds, ex.Message);
            }
        }

        metrics.Update(readings);
    }
}
