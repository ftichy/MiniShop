using System.Collections.Concurrent;
using MiniShop.TrafficGenerator;

// Generátor provozu (issue #155): virtuální zákazníci + skladník nad běžícím MiniShopem, jen přes HTTP.
// Cíl: provoz pro Grafanu a souběh ság. Platí jen dobírka a účty *.invalid (e-maily se neodesílají).
var options = Options.Parse(args);
if (options is null)
{
    return 2;
}

Console.WriteLine(options.Duration is { } duration
    ? $"Cíl {options.BaseUrl}, zákazníků {options.Customers}, délka {duration.TotalMinutes:0.#} min, pauza ~{options.PauseSeconds:0.#} s"
    : $"Cíl {options.BaseUrl}, zákazníků {options.Customers}, kol {options.Rounds}, pauza ~{options.PauseSeconds:0.#} s");

// PC se během běhu nesmí uspat – bez sítě by generátor hlásil chyby, které nezpůsobil MiniShop.
using var keepAwake = new KeepAwake();
if (keepAwake.IsActive)
{
    Console.WriteLine("PC se během běhu neuspí (obrazovka zůstane svítit); víko nezavírejte.");
}

// Konec běhu s --duration: po něm už žádný nový scénář (rozpracovaný se dokončí).
var startedAt = DateTime.UtcNow;
DateTime? deadline = options.Duration is { } length ? startedAt + length : null;

// První Ctrl+C = konec běhu bez nových scénářů (rozpracované se dokončí a vypíše se souhrn); druhé ukončí hned.
var stopRequested = false;
Console.CancelKeyPress += (_, e) =>
{
    if (stopRequested)
    {
        return;
    }

    stopRequested = true;
    deadline = DateTime.UtcNow;
    e.Cancel = true;
    Console.WriteLine("Ctrl+C: žádné nové scénáře, rozpracované se dokončí (další Ctrl+C = konec hned).");
};

// Přihlášení rozložená v čase: formuláře účtu mají limit 10 za minutu z jedné IP (na kopii Webu) – ~8 za minutu
// se vejde i se skladníkem (2 formuláře) a i kdyby Caddy poslal všechny na jednu kopii.
var loginGap = TimeSpan.FromSeconds(7.5);

var results = new ConcurrentBag<ScenarioResult>();
using var staffSession = new Session(options.BaseUrl);
try
{
    await Shop.WithRateLimitRetryAsync("Skladník", () => Shop.LoginStaffAsync(staffSession, options.Password, options.StaffTotpKey));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Skladník se nepřihlásil: {ex.Message}");
    return 1;
}

using var desk = new Desk(staffSession, options.Patience);

// E-shop krok 26C: doručení řídí dopravce (4 kroky po 30 s); ztracený webhook dožene dohledání za 2–3 min.
// Krok 26D: nedoručitelná zásilka jde 8 kroků (2× nezastižen, zpět odesílateli) = 4 min + případné dohledání.
static TimeSpan DeliveryPatience(Options options) => options.Patience > TimeSpan.FromMinutes(8) ? options.Patience : TimeSpan.FromMinutes(8);

async Task Pause(Random random)
{
    if (options.PauseSeconds > 0)
    {
        await Task.Delay(TimeSpan.FromSeconds(options.PauseSeconds * (0.5 + random.NextDouble())));
    }
}

async Task RunCustomerAsync(int number)
{
    var random = new Random(number * 7919 + Environment.TickCount);
    using var session = new Session(options.BaseUrl);
    try
    {
        await Task.Delay(loginGap * number);   // zákazník 1 po 7,5 s (skladník už je přihlášený), 20 po 2,5 min
        await Shop.WithRateLimitRetryAsync($"Zákazník {number}", () => Shop.LoginAsync(session, Shop.CustomerEmail(number), options.Password));
    }
    catch (Exception ex)
    {
        results.Add(new(number, "přihlášení", null, false, null, ex.Message));
        return;
    }

    // Zákazníci nezačínají všichni ve stejnou vteřinu.
    await Pause(random);

    for (var round = 0; deadline is { } end ? DateTime.UtcNow < end : round < options.Rounds; round++)
    {
        // Střídání: doručená, zrušená, vrácená (zákazník 1 začíná doručenou, 2 zrušenou, 3 vrácenou …).
        var scenario = ((number + round) % 3) switch { 0 => "doručená", 1 => "zrušená", _ => "vrácená" };
        int? orderId = null;
        TimeSpan? confirmTime = null;
        ScenarioResult result;
        try
        {
            await Shop.AddRandomProductsAsync(session, random);
            await Pause(random);
            orderId = await Shop.CheckoutCashOnDeliveryAsync(session);
            confirmTime = await Shop.WaitForStatusAsync(session, orderId.Value, "Potvrzená", options.Patience);
            await Pause(random);

            switch (scenario)
            {
                case "zrušená":
                    await Shop.CancelAsync(session, orderId.Value);
                    await Shop.WaitForStatusAsync(session, orderId.Value, "Zrušená", options.Patience);
                    break;
                default:
                    await desk.HandOverAsync(orderId.Value);
                    // E-shop krok 26C: doručení hlásí dopravce – 4 kroky po 30 s, ztracený webhook dožene dohledání (2–3 min).
                    await Shop.WaitForStatusAsync(session, orderId.Value, "Odeslaná", options.Patience);
                    // Krok 26D: dopravce může zásilku vrátit nedoručenou (zlobení UndeliverableRate) – i to je platný konec.
                    var (outcome, _) = await Shop.WaitForAnyStatusAsync(session, orderId.Value, DeliveryPatience(options), "Doručená", "Vráceno dopravcem");
                    if (outcome == "Vráceno dopravcem")
                    {
                        scenario += " → vráceno dopravcem";
                    }
                    else if (scenario == "vrácená")
                    {
                        await Pause(random);
                        await Shop.RequestReturnAsync(session, orderId.Value);
                        await desk.AcceptReturnAsync(orderId.Value);
                        await Shop.WaitForStatusAsync(session, orderId.Value, "Vrácená", options.Patience);
                    }

                    break;
            }

            result = new(number, scenario, orderId, true, confirmTime, null);
        }
        catch (Exception ex)
        {
            result = new(number, scenario, orderId, false, confirmTime, ex.Message);
        }

        results.Add(result);

        // Dlouhý běh: průběžně, ať je vidět, že generátor žije (tabulka je až na konci).
        if (deadline is not null)
        {
            Console.WriteLine($"[{DateTime.UtcNow - startedAt:hh\\:mm\\:ss}] zákazník {number}: {scenario} {orderId?.ToString() ?? "-"} – " +
                              $"{(result.Ok ? "OK" : "CHYBA: " + result.Error)} (celkem {results.Count} scénářů)");
        }

        await Pause(random);
    }
}

await Task.WhenAll(Enumerable.Range(1, options.Customers).Select(RunCustomerAsync));

Console.WriteLine();
Console.WriteLine("Zákazník | Scénář     | Objednávka | Potvrzení | Výsledek");
foreach (var r in results.OrderBy(r => r.Customer).ThenBy(r => r.OrderId))
{
    Console.WriteLine($"{r.Customer,8} | {r.Scenario,-10} | {r.OrderId?.ToString() ?? "-",10} | {(r.ConfirmTime is { } t ? $"{t.TotalSeconds:0.0} s" : "-"),9} | {(r.Ok ? "OK" : "CHYBA: " + r.Error)}");
}

var failed = results.Count(r => !r.Ok);
Console.WriteLine($"\nHotovo za {DateTime.UtcNow - startedAt:hh\\:mm\\:ss}: {results.Count - failed} z {results.Count} scénářů prošlo.");

// Souhrn pro dlouhý běh: scénáře podle výsledku a doba potvrzení (p50/p95 – stejně jako stránka Rychlost vyřízení).
foreach (var group in results.GroupBy(r => r.Ok ? r.Scenario : "CHYBA").OrderBy(g => g.Key))
{
    Console.WriteLine($"  {group.Key}: {group.Count()}");
}

var confirmations = results.Where(r => r.ConfirmTime is not null).Select(r => r.ConfirmTime!.Value.TotalSeconds).Order().ToList();
if (confirmations.Count > 0)
{
    double Percentile(double p) => confirmations[Math.Max(0, (int)Math.Ceiling(p * confirmations.Count) - 1)];
    Console.WriteLine($"  Potvrzení: p50 {Percentile(0.5):0.0} s, p95 {Percentile(0.95):0.0} s, nejdéle {confirmations[^1]:0.0} s ({confirmations.Count} objednávek)");
}
return failed == 0 ? 0 : 1;

internal sealed record ScenarioResult(int Customer, string Scenario, int? OrderId, bool Ok, TimeSpan? ConfirmTime, string? Error);
