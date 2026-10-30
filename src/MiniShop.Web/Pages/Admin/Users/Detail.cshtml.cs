using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Users;

/// <summary>
/// Účty krok 6: detail uživatele – jeho objednávky, historie změn a akce admina (povýšit, vrátit na usera,
/// zablokovat, odblokovat). Pravidla (poslední admin, sám sebe) hlídá <see cref="AccountAdministration"/>.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class DetailModel(AccountAdministration accounts, IOrderService orderService) : PageModel
{
    private const int AuditCount = 50;

    public AccountSummary Account { get; private set; } = null!;
    public IReadOnlyList<OrderDto> Orders { get; private set; } = [];
    public IReadOnlyList<AccountAuditItem> Audit { get; private set; } = [];
    public bool IsCurrentUser { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAccountAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        Account = account;
        IsCurrentUser = account.Id == CurrentUserId;
        // Stejné pravidlo jako zákazník na stránce Objednávky – jen objednávky tohoto uživatele (filtr v SQL).
        Orders = await orderService.GetAllAsync(OrderAccess.Customer(id), cancellationToken);
        Audit = await accounts.GetAuditAsync(id, AuditCount, cancellationToken);
        return Page();
    }

    public Task<IActionResult> OnPostPromoteAsync(string id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, accounts.PromoteToAdminAsync(CurrentUserId, id, cancellationToken));

    public Task<IActionResult> OnPostDemoteAsync(string id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, accounts.DemoteToUserAsync(CurrentUserId, id, cancellationToken));

    public Task<IActionResult> OnPostLockAsync(string id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, accounts.LockAsync(CurrentUserId, id, cancellationToken));

    public Task<IActionResult> OnPostUnlockAsync(string id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, accounts.UnlockAsync(CurrentUserId, id, cancellationToken));

    /// <summary>Post/Redirect/Get – po akci zpět na detail, výsledek přes TempData.</summary>
    private async Task<IActionResult> ExecuteAsync(string id, Task<AccountAdminResult> action)
    {
        var result = await action;
        if (result.Succeeded)
        {
            StatusMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage(new { id });
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Přihlášený uživatel nemá ID.");
}
