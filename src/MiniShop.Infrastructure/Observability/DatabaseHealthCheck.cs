using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MiniShop.Infrastructure.Observability;

/// <summary>
/// Škálování krok 3: „je databáze k dispozici?“ S PostgreSQL je databáze samostatný server (kontejner) – když neběží,
/// služba nic neuloží ani nenačte. <see cref="HealthStatus.Unhealthy"/> → /health vrátí 503, UptimeRobot pošle e-mail
/// a <c>docker compose up --wait</c> při nasazení selže (a vrátí předchozí verzi).
/// </summary>
public sealed class DatabaseHealthCheck<TDbContext>(TDbContext db) : IHealthCheck
    where TDbContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy($"Databáze ({db.Database.ProviderName}) odpovídá.")
            : HealthCheckResult.Unhealthy("Databáze neodpovídá.");
}
