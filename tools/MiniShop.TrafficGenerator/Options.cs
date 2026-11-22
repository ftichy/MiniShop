namespace MiniShop.TrafficGenerator;

/// <summary>Nastavení z argumentů (<c>--url</c>, <c>--customers</c>, <c>--rounds</c>, <c>--duration</c>, <c>--pause</c>) a proměnných prostředí (hesla a klíče nikdy do příkazové řádky).</summary>
public sealed record Options(Uri BaseUrl, int Customers, int Rounds, double PauseSeconds, string Password, string StaffTotpKey, TimeSpan Patience, TimeSpan? Duration = null)
{
    public static Options? Parse(string[] args)
    {
        string? Get(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var password = Environment.GetEnvironmentVariable("LOADTEST_PASSWORD");
        var key = Environment.GetEnvironmentVariable("LOADTEST_STAFF_TOTP_KEY");
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(key) || args.Contains("--help"))
        {
            Console.Error.WriteLine("""
                Generátor provozu MiniShopu (issue #155) – jen dobírka, jen testovací účty *.invalid.

                Nejdřív na serveru: dotnet MiniShop.Web.dll create-loadtest-users   (vypíše klíč 2FA skladníka)
                Pak (z PC):
                  LOADTEST_PASSWORD=…  LOADTEST_STAFF_TOTP_KEY=…  dotnet run --project tools/MiniShop.TrafficGenerator -- \
                      --url https://minishop-tichy.cz [--customers 4] [--rounds 3 | --duration 90] [--pause 5]

                  --customers  počet virtuálních zákazníků (1–20, výchozí 4)
                  --rounds     kolik scénářů každý zákazník projde za sebou (výchozí 3)
                  --duration   místo počtu kol délka běhu v minutách – zákazníci spouštějí další scénáře, dokud čas neuplyne
                               (rozpracovaný dokončí); přihlášení se rozloží, ~8 za minutu (limit formulářů účtu)
                  --pause      průměrná pauza mezi kroky v sekundách (výchozí 5) – ať grafy vypadají jako lidé, ne jako špička
                """);
            return null;
        }

        var url = Get("--url") ?? "https://localhost:7180";
        return new Options(
            new Uri(url.TrimEnd('/') + "/"),
            Math.Clamp(int.TryParse(Get("--customers"), out var customers) ? customers : 4, 1, 20),
            Math.Max(1, int.TryParse(Get("--rounds"), out var rounds) ? rounds : 3),
            Math.Max(0, double.TryParse(Get("--pause"), System.Globalization.CultureInfo.InvariantCulture, out var pause) ? pause : 5),
            password, key, TimeSpan.FromSeconds(90),
            double.TryParse(Get("--duration"), System.Globalization.CultureInfo.InvariantCulture, out var minutes) && minutes > 0
                ? TimeSpan.FromMinutes(minutes)
                : null);
    }
}
