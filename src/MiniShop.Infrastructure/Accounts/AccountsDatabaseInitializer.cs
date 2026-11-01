using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Persistence;

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
    /// Účty krok 6: users.db jako první databáze přešla z EnsureCreated na migrace (jsou v ní účty).
    /// Databázi z účtů kroků 1–5 (bez historie migrací) pozná <see cref="DatabaseMigrator"/> podle tabulky AspNetUsers.
    /// </summary>
    public static Task MigrateAsync(AccountsDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, "AspNetUsers", cancellationToken);
}
