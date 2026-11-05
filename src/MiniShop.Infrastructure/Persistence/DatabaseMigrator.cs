using Microsoft.EntityFrameworkCore;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// Vytvoří nebo aktualizuje schéma modulu přes EF Core migrace (e-shop krok 1; účty už od účtů kroku 6).
/// Migrate přidá jen to, co v databázi ještě chybí – co proběhlo, je zapsané v tabulce __EFMigrationsHistory ve schématu modulu.
/// </summary>
/// <remarks>
/// Do škálování kroku 3C tu byl i „baseline“ pro SQLite databáze z doby před migracemi (EnsureCreated). PostgreSQL
/// databáze vznikají rovnou přes migrace, takže stačí Migrate. Historii migrací pozná EF podle jména migrace
/// (např. 20261104174524_InitialPostgres), ne podle projektu, ve kterém migrace leží.
/// </remarks>
public static class DatabaseMigrator
{
    public static Task MigrateAsync(DbContext dbContext, CancellationToken cancellationToken = default) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}
