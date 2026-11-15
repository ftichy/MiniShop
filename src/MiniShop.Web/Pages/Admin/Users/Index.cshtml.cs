using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;
using MiniShop.Web.Ui;

namespace MiniShop.Web.Pages.Admin.Users;

/// <summary>
/// Účty krok 6: přehled uživatelů pro admina. Účty jsou v modulu Accounts (users.db), objednávky v Orderingu
/// (ordering.db) – moduly se neznají, spojí je až tahle stránka přes ID uživatele (= CustomerId objednávky).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(AccountAdministration accounts, IOrderService orderService) : PageModel
{
    private const int RecentAuditCount = 20;

    public PagedList<UserRow> UserPage { get; private set; } = new([], 1, PageRequest.DefaultSize, 0);
    public IReadOnlyList<UserRow> Users => UserPage.Items;
    public PagerModel Pager => PagerModel.From(UserPage, "users");
    public IReadOnlyList<AccountAuditItem> RecentAudit { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = PagerModel.PageParameter)]
    public int? PageNumber { get; set; }

    [BindProperty(SupportsGet = true, Name = PagerModel.SizeParameter)]
    public int? PageSize { get; set; }

    /// <summary>Hledání podle části e-mailu.</summary>
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var accountPage = await accounts.GetAccountsPageAsync(Search, PageRequest.Of(PageNumber, PageSize), cancellationToken);

        // Útrata jen pro účty na zobrazené stránce (ne pro všechny zákazníky).
        var summaries = (await orderService.GetCustomerSummariesAsync(
                accountPage.Items.Select(account => account.Id).ToList(), cancellationToken))
            .ToDictionary(summary => summary.CustomerId);

        UserPage = accountPage.Map(account => new UserRow(account, summaries.GetValueOrDefault(account.Id)));
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
