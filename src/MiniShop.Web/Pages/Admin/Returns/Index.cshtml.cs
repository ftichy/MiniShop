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
[Authorize(Policy = AuthorizationPolicies.RequireReturns)]   // účty krok 12B
public sealed class IndexModel(IOrderService orderService, ReturnSagaOrchestrator returns, IAuthorizationService authorization)
    : PageModel
{
    /// <summary>Účty krok 12B: převzetí zásilky (Přijato) – sklad.</summary>
    public bool CanAccept { get; private set; }

    /// <summary>Účty krok 12B: zamítnutí vratky (spor se zákazníkem) – podpora.</summary>
    public bool CanReject { get; private set; }

    public IReadOnlyList<ReturnRow> Rows { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanAccept = await IsAllowedAsync(AuthorizationPolicies.RequireFulfilment);
        CanReject = await IsAllowedAsync(AuthorizationPolicies.RequireSupport);
        // E-shop krok 22: řádek = jedna vratka (objednávka + číslo vratky; 0 = celá objednávka).
        var orders = (await orderService.GetReturnQueueAsync(cancellationToken)).ToDictionary(o => o.Order.Id);
        Rows = (await returns.GetOpenAsync(cancellationToken))
            .Where(saga => orders.ContainsKey(saga.OrderId))
            .Select(saga => new ReturnRow(orders[saga.OrderId], saga))
            .ToList();
    }

    public async Task<IActionResult> OnPostAcceptAsync(int id, int returnNumber, CancellationToken cancellationToken) =>
        !await IsAllowedAsync(AuthorizationPolicies.RequireFulfilment) ? Forbid() : await ExecuteAsync(id, async () => await returns.AcceptGoodsAsync(id, returnNumber, cancellationToken),
            $"Vratka č. {id} převzata – sklad zboží naskladní a zákazník dostane peníze zpět.");

    public async Task<IActionResult> OnPostRejectAsync(int id, int returnNumber, string? reason, CancellationToken cancellationToken) =>
        !await IsAllowedAsync(AuthorizationPolicies.RequireSupport) ? Forbid() : await ExecuteAsync(id, async () => await returns.RejectAsync(id, reason ?? string.Empty, returnNumber, cancellationToken),
            $"Vrácení objednávky č. {id} zamítnuto – zákazník dostane e-mail s důvodem.");

    private async Task<bool> IsAllowedAsync(string policy) => (await authorization.AuthorizeAsync(User, policy)).Succeeded;

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

/// <summary>
/// Řádek stránky Vratky – objednávka a sága jedné vratky. E-shop krok 22: <see cref="Return"/> = vratka po částech
/// (vracené kusy a částka), null = vratka celé objednávky.
/// </summary>
public sealed record ReturnRow(OrderDetailDto Detail, ReturnSaga Saga)
{
    public OrderDto Order => Detail.Order;

    public OrderReturnDto? Return => Saga.IsPartial ? Detail.Returns?.FirstOrDefault(r => r.Number == Saga.ReturnNumber) : null;

    /// <summary>Kolik se vrací – vratka po částech jen vrácené kusy, celá objednávka včetně dopravy.</summary>
    public decimal Amount => Return?.Amount ?? Order.TotalPrice;
}
