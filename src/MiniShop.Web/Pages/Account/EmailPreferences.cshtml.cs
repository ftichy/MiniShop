using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// E-shop krok 10: nastavení e-mailů – souhlas s připomínkou opuštěného košíku (výchozí vypnuto). Odkaz sem vede
/// i z každé připomínky („vypnout“). Jen zákazník – admin košík nemá.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireCustomer)]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // formulář účtu – stejný limit jako ostatní
public sealed class EmailPreferencesModel(UserManager<ApplicationUser> userManager, ILogger<EmailPreferencesModel> logger)
    : PageModel
{
    [BindProperty]
    public bool CartReminders { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        CartReminders = user.CartRemindersConsent;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        user.CartRemindersConsent = CartReminders;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Nastavení se nepodařilo uložit, zkuste to prosím znovu.");
            return Page();
        }

        // Do logu jen ID účtu (účty krok 8), ne e-mail.
        logger.LogInformation("Účet {UserId}: připomínky košíku {State}", user.Id, CartReminders ? "zapnuty" : "vypnuty");
        StatusMessage = CartReminders
            ? "Připomínky košíku jsou zapnuté."
            : "Připomínky košíku jsou vypnuté – žádnou už nepošleme.";
        return RedirectToPage();
    }
}
