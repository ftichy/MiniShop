using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Fulfilment;

/// <summary>
/// E-shop krok 6: expedice pro admina – tři fronty podle stavu objednávky: K zabalení (Confirmed),
/// K odeslání (Packing), Na cestě (Shipped). Každá akce = přechod ve stavovém automatu objednávky (doména hlídá pravidla).
/// </summary>
/// <remarks>
/// „Doručeno“ simuluje dopravce – skutečný by poslal webhook (e-shop krok 8). Souběh (zákazník mezitím zrušil)
/// skončí hláškou místo přepsání (Order.Version). Složka /Admin je zamčená i konvencí AuthorizeFolder.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(IOrderService orderService) : PageModel
{
    /// <summary>Nabídka dopravců ve formuláři odeslání (jde napsat i jiného).</summary>
    public static readonly IReadOnlyList<string> Carriers = ["PPL", "Zásilkovna", "Česká pošta", "DPD", "GLS"];

    public IReadOnlyList<OrderDto> ToPack { get; private set; } = [];
    public IReadOnlyList<OrderDto> ToShip { get; private set; } = [];
    public IReadOnlyList<OrderDto> InTransit { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var queue = await orderService.GetFulfilmentQueueAsync(cancellationToken);
        ToPack = queue.Where(o => o.Status == "Confirmed").ToList();
        ToShip = queue.Where(o => o.Status == "Packing").ToList();
        InTransit = queue.Where(o => o.Status == "Shipped").ToList();
    }

    /// <summary>Hromadně: začít balit vybrané objednávky. Každá zvlášť – chyba jedné (zrušená) nezastaví ostatní.</summary>
    public async Task<IActionResult> OnPostStartPackingAsync(int[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
        {
            ErrorMessage = "Vyberte objednávky, které chcete začít balit.";
            return RedirectToPage();
        }

        var packed = new List<int>();
        var errors = new List<string>();
        foreach (var id in ids.Distinct())
        {
            if (await TryAsync(id, () => orderService.StartPackingAsync(id, cancellationToken), errors))
            {
                packed.Add(id);
            }
        }

        StatusMessage = packed.Count > 0 ? $"Balíme: {string.Join(", ", packed.Select(id => $"č. {id}"))}." : null;
        ErrorMessage = errors.Count > 0 ? string.Join(" ", errors) : null;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostShipAsync(int id, string? carrier, string? trackingNumber, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (await TryAsync(id, () => orderService.ShipAsync(id, carrier, trackingNumber, cancellationToken), errors))
        {
            StatusMessage = $"Objednávka č. {id} odeslána.";
        }

        ErrorMessage = errors.FirstOrDefault();
        return RedirectToPage();
    }

    /// <summary>Simulace dopravce: zásilka doručena (osobní odběr: vyzvednuta).</summary>
    public async Task<IActionResult> OnPostDeliverAsync(int id, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (await TryAsync(id, () => orderService.CompleteAsync(id, cancellationToken), errors))
        {
            StatusMessage = $"Objednávka č. {id} doručena.";
        }

        ErrorMessage = errors.FirstOrDefault();
        return RedirectToPage();
    }

    private static async Task<bool> TryAsync(int id, Func<Task> action, List<string> errors)
    {
        try
        {
            await action();
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            errors.Add($"Č. {id}: {Orders.IndexModel.ConcurrencyMessage}");
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            errors.Add($"Č. {id}: {ex.Message}");
        }

        return false;
    }
}
