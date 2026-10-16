using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Domain.Products;

namespace MiniShop.Infrastructure.Persistence;

public static class DbInitializer
{
    /// <summary>
    /// Vytvoří databázi (pokud neexistuje) a naplní ji ukázkovými produkty.
    /// Pro jednoduchost používáme EnsureCreated; později lze přejít na EF migrace.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MiniShopDbContext>();

        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Products.AddRange(
            new Product("Mechanická klávesnice", "Klávesnice s hnědými spínači, CZ layout", 2490m, 15),
            new Product("Bezdrátová myš", "Ergonomická myš, 2,4 GHz + Bluetooth", 890m, 40),
            new Product("Monitor 27\"", "27\" IPS, 2560×1440, 144 Hz", 7990m, 5),
            new Product("USB-C kabel 1 m", "Nabíjecí a datový kabel, 100 W", 199m, 120),
            new Product("Sluchátka", "Uzavřená sluchátka s mikrofonem", 1590m, 12));

        await db.SaveChangesAsync(cancellationToken);
    }
}
