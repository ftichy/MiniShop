using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Domain.Inventory;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Inventory;

public static class InventoryDatabaseInitializer
{
    /// <summary>
    /// Vytvoří nebo aktualizuje databázi skladu (e-shop krok 1: EF migrace) a prázdnou naplní ukázkovými produkty.
    /// </summary>
    /// <remarks>
    /// Do kopie katalogu v Orderingu se produkty dostanou stejně jako každý jiný nový produkt –
    /// přes ProductUpdatedV1 z outboxu skladu a RabbitMQ, pár sekund po startu obou služeb.
    /// </remarks>
    public static async Task InitializeAsync(IServiceProvider services, TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await MigrateAsync(inventory, cancellationToken);

        if (await inventory.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        inventory.Products.AddRange(
            new Product("Mechanická klávesnice", "Klávesnice s hnědými spínači, CZ layout", 2490m, 15, now),
            new Product("Bezdrátová myš", "Ergonomická myš, 2,4 GHz + Bluetooth", 890m, 40, now),
            new Product("Monitor 27\"", "27\" IPS, 2560×1440, 144 Hz", 7990m, 5, now),
            new Product("USB-C kabel 1 m", "Nabíjecí a datový kabel, 100 W", 199m, 120, now),
            new Product("Sluchátka", "Uzavřená sluchátka s mikrofonem", 1590m, 12, now));

        await inventory.SaveChangesAsync(cancellationToken);   // → 5× ProductUpdatedV1 do outboxu skladu
    }

    /// <summary>Databázi z doby před migracemi pozná <see cref="DatabaseMigrator"/> podle tabulky Products.</summary>
    public static Task MigrateAsync(InventoryDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, "Products", cancellationToken);
}
