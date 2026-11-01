using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Account;

/// <summary>Účty krok 5: sem vede odkaz z e-mailu po registraci. Funguje i bez přihlášení (jiný prohlížeč, telefon).</summary>
[AllowAnonymous]
public sealed class ConfirmEmailModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<ConfirmEmailModel> logger) : PageModel
{
    public bool Confirmed { get; private set; }

    public async Task OnGetAsync(string? userId, string? code)
    {
        var token = AccountEmails.DecodeToken(code);
        var user = string.IsNullOrEmpty(userId) ? null : await userManager.FindByIdAsync(userId);
        if (user is null || token is null)
        {
            return;
        }

        Confirmed = user.EmailConfirmed || (await userManager.ConfirmEmailAsync(user, token)).Succeeded;
        if (!Confirmed)
        {
            return;
        }

        logger.LogInformation("Uživatel {UserId} potvrdil e-mail", user.Id);

        // Kdo je přihlášený jako tento uživatel, dostane novou cookie – zmizí upozornění „potvrďte e-mail“.
        if (userManager.GetUserId(User) == user.Id)
        {
            await signInManager.RefreshSignInAsync(user);
        }
    }
}
