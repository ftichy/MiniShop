using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Journeys;

namespace MiniShop.Web.Pages.Orders;

/// <summary>
/// Krok 12B: přehrání průběhu zakládání objednávky. Vstupem je číslo objednávky, scénář (snímky) připraví
/// <see cref="JourneyReplayProjection"/> a v prohlížeči ho odehraje journey-player.js.
/// </summary>
public sealed class ReplayModel(IJourneyService journeyService) : PageModel
{
    /// <summary>Číslo objednávky – z adresy (/Orders/Replay/3) nebo z formuláře (?orderId=3).</summary>
    [BindProperty(SupportsGet = true)]
    public int? OrderId { get; set; }

    public JourneyReplayDto? Replay { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (OrderId is not { } orderId)
        {
            return;
        }

        try
        {
            Replay = await journeyService.GetReplayByOrderIdAsync(orderId, cancellationToken);
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Scénář jako JSON pro přehrávač (vložený do stránky). Výchozí encoder System.Text.Json escapuje &lt; a &gt;,
    /// takže ani text, který uživatel napsal do formuláře (třeba „&lt;/script&gt;“), stránku nerozbije.
    /// </summary>
    public string ReplayJson => JsonSerializer.Serialize(Replay, JsonSerializerOptions.Web);
}
