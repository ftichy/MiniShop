using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Journeys;

namespace MiniShop.Web.Pages.Orders;

/// <summary>Krok 12A: průběh zakládání objednávky – souhrn a časová osa z projekce proudu událostí.</summary>
public sealed class JourneyModel(IJourneyService journeyService) : PageModel
{
    /// <summary>Číslo objednávky – z adresy (/Orders/Journey/3) nebo z formuláře na stránce Objednávky (?orderId=3).</summary>
    [BindProperty(SupportsGet = true)]
    public int? OrderId { get; set; }

    public OrderJourneyDto? Journey { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (OrderId is not { } orderId)
        {
            return;
        }

        try
        {
            Journey = await journeyService.GetByOrderIdAsync(orderId, cancellationToken);
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Čas od otevření formuláře pro člověka: „+12,3 s“, „+1:05,2“.</summary>
    public static string FormatOffset(long offsetMs)
    {
        var time = TimeSpan.FromMilliseconds(offsetMs);
        return time.TotalMinutes >= 1
            ? $"+{(int)time.TotalMinutes}:{time.Seconds:00},{time.Milliseconds / 100}"
            : $"+{time.TotalSeconds:0.0} s";
    }

    public static string FormatDuration(long durationMs) => FormatOffset(durationMs).TrimStart('+');

    public static string KindBadge(string kind) => kind switch
    {
        "FormOpened" => "bg-secondary",
        "FieldFocused" => "bg-light text-dark border",
        "FieldChanged" => "bg-info text-dark",
        "Clicked" => "bg-warning text-dark",
        "SubmitClicked" => "bg-primary",
        "ValidationFailed" => "bg-danger",
        "OrderSubmitted" => "bg-success",
        _ => "bg-secondary"
    };

    public static string KindText(string kind) => kind switch
    {
        "FormOpened" => "otevření",
        "FieldFocused" => "pole",
        "FieldChanged" => "změna",
        "Clicked" => "klik",
        "SubmitClicked" => "odeslání",
        "ValidationFailed" => "chyba",
        "OrderSubmitted" => "objednávka",
        _ => kind
    };
}
