using Microsoft.Extensions.DependencyInjection;

namespace MiniShop.Infrastructure.Ordering;

public static class OrderingDatabaseInitializer
{
    /// <summary>
    /// Vytvoří databázi modulu Ordering (pokud neexistuje). Pro jednoduchost EnsureCreated; později lze přejít na EF migrace.
    /// </summary>
    /// <remarks>
    /// Produkty se tu nezakládají – do kopie katalogu se dostanou z Inventory.Api přes ProductUpdatedV1 (krok 10).
    /// </remarks>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.EnsureCreatedAsync(cancellationToken);
    }
}
