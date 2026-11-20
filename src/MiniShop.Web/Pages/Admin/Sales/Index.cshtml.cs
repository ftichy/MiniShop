using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Reporting;
using MiniShop.Web.Accounts;
using MiniShop.Web.Reporting;

namespace MiniShop.Web.Pages.Admin.Sales;

/// <summary>
/// E-shop krok 11 (CQRS): přehled tržeb pro admina – čte jen čtecí model modulu Reporting, nikdy databázi objednávek.
/// Data jsou o pár sekund pozadu (projekce z událostí), proto stránka ukazuje „data k…“. Mikroservisy M1: čtecí model
/// drží samostatná služba MiniShop.Reporting.Api – stránka se ptá přes HTTP, a když služba neběží, řekne to.
/// </summary>
/// <remarks>Přestavět = smazat čtecí model a nechat si od Orderingu poslat snímky všech objednávek znovu.</remarks>
[Authorize(Policy = AuthorizationPolicies.RequireAccounting)]   // účty krok 12B
public sealed class IndexModel(ISalesReportApi reports, ILogger<IndexModel> logger) : PageModel
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    /// <summary>Null = služba přehledu je nedostupná (stránka ukáže upozornění místo chyby 500).</summary>
    public SalesReportDto? Report { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Report = await reports.GetAsync(cancellationToken);
        }
        catch (SalesReportUnavailableException ex)
        {
            logger.LogWarning(ex, "[Přehled] Služba přehledu tržeb neodpovídá");
        }
    }

    public async Task<IActionResult> OnPostRebuildAsync(CancellationToken cancellationToken)
    {
        try
        {
            await reports.RebuildAsync(cancellationToken);
            StatusMessage = "Přehled se přestavuje – Ordering posílá snímky objednávek, za pár sekund bude zase kompletní.";
        }
        catch (SalesReportUnavailableException ex)
        {
            logger.LogWarning(ex, "[Přehled] Přestavění se nepodařilo – služba přehledu tržeb neodpovídá");
            StatusMessage = "Chyba: služba přehledu tržeb teď neodpovídá – přestavění zkuste za chvíli znovu.";
        }

        return RedirectToPage();
    }

    /// <summary>Šířka pruhu v grafu (procenta nejvyšší tržby za 14 dní).</summary>
    public int BarPercent(decimal revenue)
    {
        var max = Report!.Days.Max(d => d.Revenue);
        return max <= 0 ? 0 : (int)Math.Round(revenue / max * 100);
    }

    public static string Money(decimal value) => value.ToString("N0", Czech) + " Kč";

    public static string DayName(DateOnly day) => day.ToString("ddd d. M.", Czech);

    /// <summary>Stav podle jména ze služby přehledu (mikroservisy M1: Web doménu Reportingu nezná, jen texty).</summary>
    public static string StatusName(string status) => status switch
    {
        "Placed" => "Čeká (sklad / platba)",
        "Confirmed" => "Potvrzené (i balené a odeslané)",
        "Completed" => "Doručené",
        "Rejected" => "Zamítnuté",
        "Cancelled" => "Zrušené",
        "Returned" => "Vrácené",
        "ReturnedToSender" => "Vrácené dopravcem",   // e-shop krok 26D
        _ => status
    };
}
