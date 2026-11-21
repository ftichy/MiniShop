using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Reporting;
using MiniShop.Web.Accounts;
using MiniShop.Web.Reporting;

namespace MiniShop.Web.Pages.Admin.LeadTimes;

/// <summary>
/// Proud B2: <b>Rychlost vyřízení</b> – jak dlouho trvá potvrzení, expedice a doručení (p50/p95 po dnech). Data staví
/// služba Reporting.Api jen přehráváním proudu zpráv (záložka v její databázi); „Přestavět z proudu“ přetočí záložku
/// na začátek. Pro role s přehledem objednávek (sklad, podpora, účetní).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireOrderOverview)]
public sealed class IndexModel(ILeadTimeApi leadTimes, ILogger<IndexModel> logger) : PageModel
{
    public LeadTimeReportDto? Report { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Report = await leadTimes.GetLeadTimesAsync(cancellationToken);
        }
        catch (SalesReportUnavailableException ex)
        {
            logger.LogWarning("Rychlost vyřízení nedostupná: {Error}", ex.InnerException?.Message ?? ex.Message);
            ErrorMessage = "Služba přehledů (reporting-api) teď neodpovídá – zkuste to za chvíli znovu.";
        }
    }

    public async Task<IActionResult> OnPostRebuildAsync(CancellationToken cancellationToken)
    {
        try
        {
            await leadTimes.RebuildLeadTimesAsync(cancellationToken);
            StatusMessage = "Přehled se přestavuje z proudu zpráv – za pár sekund bude zase kompletní (jen historie, kterou proud ještě drží).";
        }
        catch (SalesReportUnavailableException ex)
        {
            logger.LogWarning("Přestavění Rychlosti vyřízení selhalo: {Error}", ex.InnerException?.Message ?? ex.Message);
            StatusMessage = "Chyba: služba přehledů teď neodpovídá – přestavění zkuste za chvíli znovu.";
        }

        return RedirectToPage();
    }

    /// <summary>Doba pro člověka: „4,2 s“, „3 min 20 s“, „2 h 5 min“, „—“ (žádná objednávka).</summary>
    public static string Format(double? seconds) => seconds switch
    {
        null => "—",
        < 60 => $"{seconds.Value:0.0} s",
        < 3600 => $"{(int)(seconds.Value / 60)} min {(int)(seconds.Value % 60)} s",
        _ => $"{(int)(seconds.Value / 3600)} h {(int)(seconds.Value % 3600 / 60)} min"
    };
}
