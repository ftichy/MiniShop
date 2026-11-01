using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// Vytvoří nebo aktualizuje databázi modulu přes EF Core migrace (e-shop krok 1; users.db už od účtů kroku 6).
/// Migrate přidá jen to, co v databázi ještě chybí – co proběhlo, je zapsané v tabulce __EFMigrationsHistory.
/// </summary>
/// <remarks>
/// Databáze z doby před migracemi vznikly přes EnsureCreated: tabulky mají, historii migrací ne. Takové databázi
/// zapíšeme první migraci jako „už provedenou“ (baseline – odpovídá právě tomuto schématu), jinak by ji Migrate
/// spustil znovu a spadl na existující tabulce. Starou databázi poznáme podle značkové tabulky modulu
/// (např. Orders). Nová databáze tím neprojde – vytvoří ji celou Migrate.
/// </remarks>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(DbContext dbContext, string markerTable, CancellationToken cancellationToken = default)
    {
        await BaselineDatabaseCreatedWithoutMigrationsAsync(dbContext, markerTable, cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task BaselineDatabaseCreatedWithoutMigrationsAsync(
        DbContext dbContext, string markerTable, CancellationToken cancellationToken)
    {
        var history = dbContext.GetService<IHistoryRepository>();
        if (await history.ExistsAsync(cancellationToken) || !await TableExistsAsync(dbContext, markerTable, cancellationToken))
        {
            return;
        }

        var initialMigration = dbContext.Database.GetMigrations().First();
        await dbContext.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(initialMigration, ProductInfo.GetVersion())), cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(DbContext dbContext, string table, CancellationToken cancellationToken) =>
        await dbContext.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {table}")
            .SingleAsync(cancellationToken) > 0;
}
