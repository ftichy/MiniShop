using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Journeys;
using MiniShop.Web.Accounts;

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
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]   // účty krok 2: průběh vidí jen admin
    public async Task<ActionResult<OrderJourneyDto>> GetByOrder(int orderId, CancellationToken cancellationToken) =>
        Ok(await journeyService.GetByOrderIdAsync(orderId, cancellationToken));

    /// <summary>Krok 12B: scénář přehrávání – události s časem přehrání (dlouhé pauzy zkrácené).</summary>
    [HttpGet("orders/{orderId:int}/replay")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    public async Task<ActionResult<JourneyReplayDto>> GetReplay(int orderId, CancellationToken cancellationToken) =>
        Ok(await journeyService.GetReplayByOrderIdAsync(orderId, cancellationToken));
}
