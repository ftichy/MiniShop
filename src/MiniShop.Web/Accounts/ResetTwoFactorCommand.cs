using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// 2FA krok A2: nouzový příkaz při ztrátě telefonu (a záchranných kódů) – místo webové aplikace se spustí jen tohle:
/// <code>dotnet MiniShop.Web.dll reset-2fa --email admin@example.com</code>
/// V cloudu v běžícím kontejneru (<c>docker compose exec web dotnet MiniShop.Web.dll reset-2fa --email …</c>).
/// Dvoufázové ověření účtu se zruší, všechna přihlášení skončí a zapíše se audit. Postup: docs/prirucka/architektura/moduly/accounts.md.
/// </summary>
public static class ResetTwoFactorCommand
{
    public const string Name = "reset-2fa";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var index = Array.IndexOf(args, "--email");
        var email = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine($"Použití: dotnet MiniShop.Web.dll {Name} --email <e-mail>");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        await AccountsDatabaseInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<AccountsDbContext>());

        var result = await scope.ServiceProvider.GetRequiredService<TwoFactorReset>().ResetAsync(email);
        if (result.Succeeded)
        {
            Console.WriteLine(result.Message);
            return 0;
        }

        Console.Error.WriteLine(result.Message);
        return 1;
    }
}
