using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Ordering;

public static class OrderingDatabaseInitializer
{
    /// <summary>
    /// Vytvoří nebo aktualizuje databázi modulu Ordering. E-shop krok 1: EF migrace místo EnsureCreated –
    /// v produkci jsou skutečné objednávky, změna schématu = nová migrace, ne smazání databáze.
    /// </summary>
    /// <remarks>
    /// Produkty se tu nezakládají – do kopie katalogu se dostanou z Inventory.Api přes ProductUpdatedV1 (krok 10).
    /// </remarks>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<OrderingDbContext>(), cancellationToken);
    }

    /// <summary>Databázi z doby před migracemi pozná <see cref="DatabaseMigrator"/> podle tabulky Orders.</summary>
    public static Task MigrateAsync(OrderingDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, "Orders", cancellationToken);
}
