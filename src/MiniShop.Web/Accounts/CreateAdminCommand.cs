using System.Text;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Účty krok 2: jednorázový příkaz, který založí prvního admina – místo webové aplikace se spustí jen tohle:
/// <code>dotnet MiniShop.Web.dll create-admin --email admin@example.com [--name "Jan Novák"]</code>
/// V cloudu přímo v běžícím kontejneru (<c>docker compose exec web …</c>). Heslo se píše do terminálu
/// (skrytě), takže nikdy neleží v GitHubu, v .env ani v historii příkazů.
/// </summary>
public static class CreateAdminCommand
{
    public const string Name = "create-admin";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var email = GetOption(args, "--email");
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine($"Použití: dotnet MiniShop.Web.dll {Name} --email <e-mail> [--name <jméno>]");
            return 2;
        }

        var displayName = GetOption(args, "--name") ?? "Administrátor";

        var password = ReadPassword("Heslo: ");
        if (password != ReadPassword("Heslo znovu: "))
        {
            Console.Error.WriteLine("Hesla se neshodují, admin nevznikl.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();

        // users.db musí existovat (a mít aktuální schéma) i tehdy, když Web ještě nikdy neběžel.
        // Role si připraví CreateFirstAdminAsync.
        await AccountsDatabaseInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<AccountsDbContext>());

        var seeder = scope.ServiceProvider.GetRequiredService<AccountsSeeder>();
        var result = await seeder.CreateFirstAdminAsync(email, displayName, password, mustChangePassword: false);

        if (!result.Succeeded)
        {
            foreach (var description in result.Errors.Select(error => error.Description).Distinct())
            {
                Console.Error.WriteLine(description);
            }

            return 1;
        }

        Console.WriteLine($"Admin {email} je založený. Přihlaste se na /Account/Login.");
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>Heslo bez zobrazení na obrazovce. Přesměrovaný vstup (skript, test) se čte jako obyčejný řádek.</summary>
    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine() ?? string.Empty;
            Console.WriteLine();
            return line;
        }

        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }
}
