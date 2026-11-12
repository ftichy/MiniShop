using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>Druhý krok přihlášení záchranným kódem (ztracený telefon). Každý kód jde použít jen jednou.</summary>
[AllowAnonymous]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]
public sealed class LoginWithRecoveryCodeModel(
    SignInManager<ApplicationUser> signInManager,
    ILogger<LoginWithRecoveryCodeModel> logger) : PageModel
{
    [BindProperty]
    public RecoveryInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        return user is null ? RedirectToPage("./Login", new { ReturnUrl }) : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(TwoFactorHelper.NormalizeRecoveryCode(Input.RecoveryCode));
        if (result.Succeeded)
        {
            logger.LogWarning("Uživatel {UserId} se přihlásil záchranným kódem", user.Id);
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Content("~/"));
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("Přihlášení záchranným kódem k zamčenému účtu {UserId}", user.Id);
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        logger.LogWarning("Neplatný záchranný kód při přihlášení k účtu {UserId}", user.Id);
        ModelState.AddModelError(string.Empty, "Neplatný záchranný kód.");
        return Page();
    }

    public sealed class RecoveryInput
    {
        [Required(ErrorMessage = "Záchranný kód je povinný.")]
        [StringLength(40, ErrorMessage = "Neplatný záchranný kód.")]
        [Display(Name = "Záchranný kód")]
        public string RecoveryCode { get; set; } = string.Empty;
    }
}
