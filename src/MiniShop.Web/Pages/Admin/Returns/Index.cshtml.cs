using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Returns;

/// <summary>
/// E-shop krok 12: vratky pro admina – příjem vráceného zboží. „Přijato“ a „Zamítnout“ jsou kroky člověka uprostřed
/// ságy vratky (<see cref="ReturnSagaOrchestrator"/>); naskladnění a vrácení peněz pak běží samo přes zprávy.
/// </summary>
/// <remarks>Souběh (vrácení mezitím vypršelo) skončí hláškou místo přepsání (ReturnSaga.Version).</remarks>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(IOrderService orderService, ReturnSagaOrchestrator returns) : PageModel
{
    public IReadOnlyList<ReturnRow> Rows { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var sagas = (await returns.GetOpenAsync(cancellationToken)).ToDictionary(s => s.OrderId);
        Rows = (await orderService.GetReturnQueueAsync(cancellationToken))
            .Where(o => sagas.ContainsKey(o.Id))
            .Select(o => new ReturnRow(o, sagas[o.Id]))
            .ToList();
    }

    public Task<IActionResult> OnPostAcceptAsync(int id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async () => await returns.AcceptGoodsAsync(id, cancellationToken),
            $"Vratka č. {id} převzata – sklad zboží naskladní a zákazník dostane peníze zpět.");

    public Task<IActionResult> OnPostRejectAsync(int id, string? reason, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async () => await returns.RejectAsync(id, reason ?? string.Empty, cancellationToken),
            $"Vrácení objednávky č. {id} zamítnuto – zákazník dostane e-mail s důvodem.");

    private async Task<IActionResult> ExecuteAsync(int id, Func<Task<bool>> action, string success)
    {
        try
        {
            if (await action())
            {
                StatusMessage = success;
            }
            else
            {
                ErrorMessage = $"Vratka č. {id} už na zboží nečeká (mezitím se vyřídila nebo vypršela).";
            }
        }
        catch (Exception ex) when (ex is DomainException or ConcurrencyConflictException)
        {
            ErrorMessage = ex is ConcurrencyConflictException
                ? $"Vratka č. {id} se mezitím změnila – zkontrolujte ji prosím."
                : ex.Message;
        }

        return RedirectToPage();
    }

    public static string StateText(ReturnSagaState state) => state switch
    {
        ReturnSagaState.AwaitingGoods => "Čeká na zboží",
        ReturnSagaState.Restocking => "Naskladňuje se",
        ReturnSagaState.Refunding => "Vrací se peníze",
        _ => state.ToString()
    };
}

/// <summary>Řádek stránky Vratky – objednávka a stav její ságy vratky.</summary>
public sealed record ReturnRow(OrderDto Order, ReturnSaga Saga);
