using System.Security.Claims;
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
[Authorize(Policy = AuthorizationPolicies.RequireFulfilment)]   // účty krok 12B
public sealed class IndexModel(IOrderService orderService) : PageModel
{
    /// <summary>
    /// Účty krok 12C: vlastní objednávka skladníka (podle e-mailu účtu – jen pro obrazovku; server pravidlo hlídá podle ID
    /// účtu v OrderService) – místo akcí štítek „vyřídí kolega“.
    /// </summary>
    public bool IsOwn(OrderDto order) =>
        !string.IsNullOrEmpty(order.CustomerEmail)
        && string.Equals(order.CustomerEmail, User.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase);

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
            if (await TryAsync(id, () => orderService.StartPackingAsync(id, User.ToActingUserId(), cancellationToken), errors))
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
        if (await TryAsync(id, () => orderService.ShipAsync(id, carrier, trackingNumber, User.ToActingUserId(), cancellationToken), errors))
        {
            StatusMessage = $"Objednávka č. {id} odeslána.";
        }

        ErrorMessage = errors.FirstOrDefault();
        return RedirectToPage();
    }

    /// <summary>
    /// E-shop krok 26B: předat zabalenou objednávku (kurýr) dopravci. Číslo zásilky přidělí dopravce – přijde zprávou
    /// za pár sekund (stránka se sama obnoví); odmítnutí se ukáže u objednávky s možností zkusit znovu.
    /// </summary>
    public async Task<IActionResult> OnPostHandOverAsync(int id, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (await TryAsync(id, () => orderService.RequestShipmentAsync(id, User.ToActingUserId(), cancellationToken), errors))
        {
            StatusMessage = $"Objednávka č. {id} se předává dopravci – číslo zásilky přijde za chvíli.";
        }

        ErrorMessage = errors.FirstOrDefault();
        return RedirectToPage();
    }

    /// <summary>Simulace dopravce: zásilka doručena (osobní odběr: vyzvednuta).</summary>
    public async Task<IActionResult> OnPostDeliverAsync(int id, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (await TryAsync(id, () => orderService.CompleteAsync(id, User.ToActingUserId(), cancellationToken), errors))
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
