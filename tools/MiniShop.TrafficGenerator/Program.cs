using System.Collections.Concurrent;
using MiniShop.TrafficGenerator;

// Generátor provozu (issue #155): virtuální zákazníci + skladník nad běžícím MiniShopem, jen přes HTTP.
// Cíl: provoz pro Grafanu a souběh ság. Platí jen dobírka a účty *.invalid (e-maily se neodesílají).
var options = Options.Parse(args);
if (options is null)
{
    return 2;
}

Console.WriteLine($"Cíl {options.BaseUrl}, zákazníků {options.Customers}, kol {options.Rounds}, pauza ~{options.PauseSeconds:0.#} s");

var results = new ConcurrentBag<ScenarioResult>();
using var staffSession = new Session(options.BaseUrl);
try
{
    await Shop.LoginStaffAsync(staffSession, options.Password, options.StaffTotpKey);
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
        await Shop.LoginAsync(session, Shop.CustomerEmail(number), options.Password);
    }
    catch (Exception ex)
    {
        results.Add(new(number, "přihlášení", null, false, null, ex.Message));
        return;
    }

    // Zákazníci nezačínají všichni ve stejnou vteřinu.
    await Pause(random);

    for (var round = 0; round < options.Rounds; round++)
    {
        // Střídání: doručená, zrušená, vrácená (zákazník 1 začíná doručenou, 2 zrušenou, 3 vrácenou …).
        var scenario = ((number + round) % 3) switch { 0 => "doručená", 1 => "zrušená", _ => "vrácená" };
        int? orderId = null;
        TimeSpan? confirmTime = null;
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

            results.Add(new(number, scenario, orderId, true, confirmTime, null));
        }
        catch (Exception ex)
        {
            results.Add(new(number, scenario, orderId, false, confirmTime, ex.Message));
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
Console.WriteLine($"\nHotovo: {results.Count - failed} z {results.Count} scénářů prošlo.");
return failed == 0 ? 0 : 1;

internal sealed record ScenarioResult(int Customer, string Scenario, int? OrderId, bool Ok, TimeSpan? ConfirmTime, string? Error);
