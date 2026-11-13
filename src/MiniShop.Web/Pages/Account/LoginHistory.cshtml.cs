using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Pages.Account;

/// <summary>2FA krok B: uživatel vidí posledních pár pokusů o přihlášení ke svému účtu (jen vlastní, ID z cookie).</summary>
public sealed class LoginHistoryModel(
    UserManager<ApplicationUser> userManager,
    LoginAttemptRecorder loginAttempts) : PageModel
{
    public const int Count = 20;
    public static int RetentionDays => (int)LoginAttemptRecorder.Retention.TotalDays;

    public IReadOnlyList<LoginAttemptItem> Attempts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Challenge();
        }

        Attempts = await loginAttempts.GetRecentAsync(userId, Count, cancellationToken);
        return Page();
    }
}
