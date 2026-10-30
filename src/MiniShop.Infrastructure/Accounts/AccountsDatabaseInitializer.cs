using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MiniShop.Infrastructure.Accounts;

public static class AccountsDatabaseInitializer
{
    /// <summary>
    /// Vytvoří nebo aktualizuje databázi users.db. Účty krok 2: pak připraví role a případně prvního admina
    /// z konfigurace (<see cref="AccountsSeeder"/>).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, AdminBootstrapSettings? adminBootstrap = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<AccountsDbContext>(), cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AccountsSeeder>().SeedAsync(adminBootstrap, cancellationToken);
    }

    /// <summary>
    /// Účty krok 6: users.db se na rozdíl od ostatních databází nemaže (jsou v ní účty), proto migrace místo
    /// EnsureCreated. Migrate přidá jen to, co v databázi ještě chybí (tabulka __EFMigrationsHistory).
    /// </summary>
    public static async Task MigrateAsync(AccountsDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await BaselineDatabaseCreatedWithoutMigrationsAsync(dbContext, cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// users.db z účtů kroků 1–5 vznikla přes EnsureCreated – tabulky má, historii migrací ne. Zapíšeme do ní
    /// první migraci jako „už provedenou“ (odpovídá právě tomuto schématu), jinak by ji Migrate spustil znovu
    /// a spadl na existující tabulce AspNetUsers. Nová databáze tím neprojde – vytvoří ji celou Migrate.
    /// </summary>
    private static async Task BaselineDatabaseCreatedWithoutMigrationsAsync(
        AccountsDbContext dbContext, CancellationToken cancellationToken)
    {
        var history = dbContext.GetService<IHistoryRepository>();
        if (await history.ExistsAsync(cancellationToken) || !await UsersTableExistsAsync(dbContext, cancellationToken))
        {
            return;
        }

        var initialMigration = dbContext.Database.GetMigrations().First();
        await dbContext.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(initialMigration, ProductInfo.GetVersion())), cancellationToken);
    }

    private static async Task<bool> UsersTableExistsAsync(AccountsDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'AspNetUsers'")
            .SingleAsync(cancellationToken) > 0;
}
