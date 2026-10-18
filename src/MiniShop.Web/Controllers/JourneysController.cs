using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Journeys;

namespace MiniShop.Web.Controllers;

/// <summary>
/// Krok 12A: průběh zakládání objednávky. Prohlížeč sem posílá dávky událostí (order-form-tracker.js),
/// průběh pro konkrétní objednávku vrací projekce.
/// </summary>
[ApiController]
[Route("api/journeys")]
public sealed class JourneysController(IJourneyService journeyService) : ControllerBase
{
    /// <summary>Dávka událostí z formuláře. Opakované poslání stejné dávky nevadí (idempotence podle EventId).</summary>
    [HttpPost("{journeyId:guid}/events")]
    public async Task<IActionResult> Record(Guid journeyId, IReadOnlyList<RecordJourneyEventRequest> events,
        CancellationToken cancellationToken)
    {
        await journeyService.RecordAsync(journeyId, events, cancellationToken);
        return NoContent();
    }

    /// <summary>Průběh zakládání objednávky: souhrn, časová osa (kroky) a všechny události pro přehrání.</summary>
    [HttpGet("orders/{orderId:int}")]
    public async Task<ActionResult<OrderJourneyDto>> GetByOrder(int orderId, CancellationToken cancellationToken) =>
        Ok(await journeyService.GetByOrderIdAsync(orderId, cancellationToken));
}
