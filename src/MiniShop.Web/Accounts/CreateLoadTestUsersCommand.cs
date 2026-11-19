using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Generátor provozu (issue #155): jednorázový příkaz, který založí testovací účty (zákazníci + skladník s 2FA):
/// <code>dotnet MiniShop.Web.dll create-loadtest-users [--count 4]</code>
/// V cloudu v běžícím kontejneru (<c>docker compose exec web …</c>). Heslo se píše do terminálu, klíč 2FA skladníka se vypíše.
/// </summary>
public static class CreateLoadTestUsersCommand
{
    public const string Name = "create-loadtest-users";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var count = 4;
        var index = Array.IndexOf(args, "--count");
        if (index >= 0 && (index + 1 >= args.Length || !int.TryParse(args[index + 1], out count) || count is < 1 or > 20))
        {
            Console.Error.WriteLine($"Použití: dotnet MiniShop.Web.dll {Name} [--count 1–20]");
            return 2;
        }

        var password = CreateAdminCommand.ReadPassword("Heslo testovacích účtů: ");
        await using var scope = services.CreateAsyncScope();
        await AccountsDatabaseInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<AccountsDbContext>());

        var result = await scope.ServiceProvider.GetRequiredService<LoadTestAccountSeeder>().SeedAsync(count, password);
        foreach (var error in result.Errors.Distinct())
        {
            Console.Error.WriteLine(error);
        }

        if (result.Errors.Count > 0)
        {
            return 1;
        }

        Console.WriteLine($"Zákazníci: {string.Join(", ", result.CustomerEmails)}");
        Console.WriteLine($"Skladník: {result.StaffEmail}");
        Console.WriteLine($"Klíč 2FA skladníka (do LOADTEST_STAFF_TOTP_KEY): {result.StaffTotpKey}");
        Console.WriteLine($"Nově založeno účtů: {result.Created}. E-maily na doménu .invalid se nikdy neodesílají.");
        return 0;
    }
}
