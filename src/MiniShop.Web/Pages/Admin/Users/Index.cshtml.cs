using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Users;

/// <summary>
/// Účty krok 6: přehled uživatelů pro admina. Účty jsou v modulu Accounts (users.db), objednávky v Orderingu
/// (ordering.db) – moduly se neznají, spojí je až tahle stránka přes ID uživatele (= CustomerId objednávky).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(AccountAdministration accounts, IOrderService orderService) : PageModel
{
    private const int RecentAuditCount = 20;

    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public IReadOnlyList<AccountAuditItem> RecentAudit { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var summaries = (await orderService.GetCustomerSummariesAsync(cancellationToken))
            .ToDictionary(summary => summary.CustomerId);

        Users = (await accounts.GetAccountsAsync(cancellationToken))
            .Select(account => new UserRow(account, summaries.GetValueOrDefault(account.Id)))
            .ToList();
        RecentAudit = await accounts.GetAuditAsync(targetUserId: null, RecentAuditCount, cancellationToken);
    }

    public static string ActionText(AccountAuditAction action) => action switch
    {
        AccountAuditAction.PromotedToAdmin => "povýšen na admina",
        AccountAuditAction.DemotedToUser => "vrácen na usera",
        AccountAuditAction.Locked => "zablokován",
        AccountAuditAction.Unlocked => "odblokován",
        _ => action.ToString()
    };

    public sealed record UserRow(AccountSummary Account, CustomerOrderSummaryDto? Orders)
    {
        public int OrderCount => Orders?.OrderCount ?? 0;
        public decimal TotalSpent => Orders?.TotalSpent ?? 0;
    }
}
