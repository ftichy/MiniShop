using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Reporting;
using MiniShop.Domain.Reporting;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Sales;

/// <summary>
/// E-shop krok 11 (CQRS): přehled tržeb pro admina – čte jen čtecí model modulu Reporting (reporting.db), nikdy
/// databázi objednávek. Data jsou o pár sekund pozadu (projekce z událostí), proto stránka ukazuje „data k…“.
/// </summary>
/// <remarks>Přestavět = smazat čtecí model a nechat si od Orderingu poslat snímky všech objednávek znovu.</remarks>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(SalesReportService reports) : PageModel
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    public SalesReportDto Report { get; private set; } = null!;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Report = await reports.GetAsync(cancellationToken);

    public async Task<IActionResult> OnPostRebuildAsync(CancellationToken cancellationToken)
    {
        await reports.RebuildAsync(cancellationToken);
        StatusMessage = "Přehled se přestavuje – Ordering posílá snímky objednávek, za pár sekund bude zase kompletní.";
        return RedirectToPage();
    }

    /// <summary>Šířka pruhu v grafu (procenta nejvyšší tržby za 14 dní).</summary>
    public int BarPercent(decimal revenue)
    {
        var max = Report.Days.Max(d => d.Revenue);
        return max <= 0 ? 0 : (int)Math.Round(revenue / max * 100);
    }

    public static string Money(decimal value) => value.ToString("N0", Czech) + " Kč";

    public static string DayName(DateOnly day) => day.ToString("ddd d. M.", Czech);

    public static string StatusName(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.Placed => "Čeká (sklad / platba)",
        SalesOrderStatus.Confirmed => "Potvrzené (i balené a odeslané)",
        SalesOrderStatus.Completed => "Doručené",
        SalesOrderStatus.Rejected => "Zamítnuté",
        SalesOrderStatus.Cancelled => "Zrušené",
        SalesOrderStatus.Returned => "Vrácené",
        _ => status.ToString()
    };
}
