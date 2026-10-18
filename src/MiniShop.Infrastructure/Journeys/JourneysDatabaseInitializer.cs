using Microsoft.Extensions.DependencyInjection;

namespace MiniShop.Infrastructure.Journeys;

public static class JourneysDatabaseInitializer
{
    /// <summary>Vytvoří databázi journeys.db (pokud neexistuje). Pro jednoduchost EnsureCreated, jako ostatní moduly.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<JourneysDbContext>().Database.EnsureCreatedAsync(cancellationToken);
    }
}
