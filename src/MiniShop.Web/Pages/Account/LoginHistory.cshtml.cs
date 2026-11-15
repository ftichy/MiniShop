using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Abstractions;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Ui;

namespace MiniShop.Web.Pages.Account;

/// <summary>2FA krok B: uživatel vidí pokusy o přihlášení ke svému účtu (jen vlastní, ID z cookie) – po stránkách.</summary>
public sealed class LoginHistoryModel(
    UserManager<ApplicationUser> userManager,
    LoginAttemptRecorder loginAttempts) : PageModel
{
    public static int RetentionDays => (int)LoginAttemptRecorder.Retention.TotalDays;

    public PagedList<LoginAttemptItem> AttemptPage { get; private set; } = new([], 1, PageRequest.DefaultSize, 0);
    public IReadOnlyList<LoginAttemptItem> Attempts => AttemptPage.Items;
    public PagerModel Pager => PagerModel.From(AttemptPage, "login-history");

    [BindProperty(SupportsGet = true, Name = PagerModel.PageParameter)]
    public int? PageNumber { get; set; }

    [BindProperty(SupportsGet = true, Name = PagerModel.SizeParameter)]
    public int? PageSize { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Challenge();
        }

        AttemptPage = await loginAttempts.GetPageAsync(userId, PageRequest.Of(PageNumber, PageSize), cancellationToken);
        return Page();
    }
}
