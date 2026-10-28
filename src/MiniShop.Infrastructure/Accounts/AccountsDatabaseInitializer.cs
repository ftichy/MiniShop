using Microsoft.Extensions.DependencyInjection;

namespace MiniShop.Infrastructure.Accounts;

public static class AccountsDatabaseInitializer
{
    /// <summary>Vytvoří databázi users.db (pokud neexistuje). Pro jednoduchost EnsureCreated, jako ostatní moduly.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.EnsureCreatedAsync(cancellationToken);
    }
}
