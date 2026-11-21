using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Reporting;

namespace MiniShop.Reporting.Api.Controllers;

/// <summary>
/// Proud B2: přehled Rychlost vyřízení pro stránku /Admin/LeadTimes ve Webu (servisní klíč – kdo smí, rozhoduje Web).
/// Data plní jen čtenář proudu <c>LeadTimeProjectionWorker</c>; přestavění = záložka na začátek proudu.
/// </summary>
[ApiController]
[Route("api/lead-times")]
public sealed class LeadTimesController(LeadTimeReportService reports) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LeadTimeReportDto>> Get(CancellationToken cancellationToken) =>
        Ok(await reports.GetAsync(cancellationToken));

    /// <summary>Smazat data a přetočit záložku na začátek proudu – čtenář přehraje celou historii (retence 14 dní).</summary>
    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken)
    {
        await reports.RebuildAsync(cancellationToken);
        return Accepted();
    }
}
