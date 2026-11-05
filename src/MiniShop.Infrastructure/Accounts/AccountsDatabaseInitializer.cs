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

    /// <summary>Účty krok 6: účty jako první přešly z EnsureCreated na migrace (škálování krok 3C: schéma accounts v PostgreSQL).</summary>
    public static Task MigrateAsync(AccountsDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, cancellationToken);
}
