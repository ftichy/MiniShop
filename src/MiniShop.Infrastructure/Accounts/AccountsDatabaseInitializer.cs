using Microsoft.Extensions.DependencyInjection;

namespace MiniShop.Infrastructure.Accounts;

public static class AccountsDatabaseInitializer
{
    /// <summary>
    /// Vytvoří databázi users.db (pokud neexistuje). Pro jednoduchost EnsureCreated, jako ostatní moduly.
    /// Účty krok 2: pak připraví role a případně prvního admina z konfigurace (<see cref="AccountsSeeder"/>).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, AdminBootstrapSettings? adminBootstrap = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.EnsureCreatedAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AccountsSeeder>().SeedAsync(adminBootstrap, cancellationToken);
    }
}
