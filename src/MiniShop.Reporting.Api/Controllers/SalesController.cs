using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Reporting;

namespace MiniShop.Reporting.Api.Controllers;

/// <summary>
/// Mikroservisy M1: přehled tržeb pro stránku /Admin/Sales ve Webu. Jen se servisním klíčem (celé /api) – kdo z lidí
/// přehled smí vidět (role Účetní), rozhoduje Web.
/// </summary>
[ApiController]
[Route("api/sales")]
public sealed class SalesController(SalesReportService reports) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SalesReportDto>> Get(CancellationToken cancellationToken) =>
        Ok(await reports.GetAsync(cancellationToken));

    /// <summary>Smazat čtecí model a požádat Ordering o snímky objednávek (RebuildSalesReportV1). Výsledek je za pár sekund.</summary>
    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken)
    {
        await reports.RebuildAsync(cancellationToken);
        return Accepted();
    }
}
