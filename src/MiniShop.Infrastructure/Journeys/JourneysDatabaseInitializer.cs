using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Journeys;

public static class JourneysDatabaseInitializer
{
    /// <summary>Vytvoří nebo aktualizuje databázi journeys.db. E-shop krok 1: EF migrace, jako ostatní moduly.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<JourneysDbContext>(), cancellationToken);
    }

    /// <summary>Databázi z doby před migracemi pozná <see cref="DatabaseMigrator"/> podle tabulky JourneyEvents.</summary>
    public static Task MigrateAsync(JourneysDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, "JourneyEvents", cancellationToken);
}
