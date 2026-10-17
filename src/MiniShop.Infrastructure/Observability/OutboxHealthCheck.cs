using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Observability;

/// <summary>
/// Krok 11: "visí pošta?" Hlásí <see cref="HealthStatus.Degraded"/>, když v outboxu modulu leží nedoručená zpráva
/// starší než <see cref="MaxAge"/> – typicky neběží broker, nebo zpráva propadla jako poison message.
/// </summary>
/// <remarks>
/// Běžně zpráva v outboxu leží nejvýš pár sekund (kolo workeru = 2 s). Pět minut už znamená, že ji je potřeba
/// prozkoumat (sloupec Error). Degraded, ne Unhealthy: služba dál přijímá požadavky a restart by nepomohl.
/// Proto je jen v /health, ne v /alive (podle /alive by orchestrátor službu restartoval).
/// </remarks>
public sealed class OutboxHealthCheck<TDbContext>(TDbContext db, TimeProvider timeProvider) : IHealthCheck
    where TDbContext : ModuleDbContext
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var threshold = timeProvider.GetUtcNow().UtcDateTime - MaxAge;
        var stuck = db.OutboxMessages.Where(m => m.ProcessedAtUtc == null && m.OccurredAtUtc < threshold);

        var count = await stuck.CountAsync(cancellationToken);
        if (count == 0)
        {
            return HealthCheckResult.Healthy("Outbox se průběžně vyprazdňuje.");
        }

        var oldest = await stuck.MinAsync(m => m.OccurredAtUtc, cancellationToken);
        return HealthCheckResult.Degraded(
            $"V outboxu čeká {count} zpráv déle než {MaxAge.TotalMinutes:0} min (nejstarší z {oldest:u}).",
            data: new Dictionary<string, object> { ["count"] = count, ["oldestUtc"] = oldest });
    }
}
