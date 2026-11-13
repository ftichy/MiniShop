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

/// <summary>
/// Dvoufázové ověření, druhý krok přihlášení: heslo už prošlo (Identity si to pamatuje v dočasné cookie),
/// teď 6místný kód z aplikace. Špatný kód se počítá do zámku účtu stejně jako špatné heslo.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]
public sealed class LoginWith2faModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    LoginAttemptRecorder loginAttempts,
    ILogger<LoginWith2faModel> logger) : PageModel
{
    [BindProperty]
    public TwoFactorInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    /// <summary>Zapamatovat zařízení nabízíme jen zákazníkovi – admin zadává kód vždy (<see cref="TwoFactorHelper.MayRememberDevice"/>).</summary>
    public bool CanRememberDevice { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        CanRememberDevice = TwoFactorHelper.MayRememberDevice(await userManager.GetRolesAsync(user));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            // Dočasná cookie po hesle vypršela (nebo ji nikdo nemá) – začít znovu.
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        CanRememberDevice = TwoFactorHelper.MayRememberDevice(await userManager.GetRolesAsync(user));
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var code = TwoFactorHelper.NormalizeCode(Input.Code);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            code, RememberMe, Input.RememberMachine && CanRememberDevice);
        await loginAttempts.RecordAsync(HttpContext, user, LoginMethod.AuthenticatorCode,
            LoginAudit.Map(result, user, LoginResult.WrongCode));

        if (result.Succeeded)
        {
            logger.LogInformation("Uživatel {UserId} se přihlásil kódem z aplikace", user.Id);
            return LocalRedirect(SafeReturnUrl());
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("Přihlášení kódem k zamčenému účtu {UserId}", user.Id);
            return RedirectToPage("./Login", new { ReturnUrl });
        }

        logger.LogWarning("Neplatný kód při přihlášení k účtu {UserId}", user.Id);
        ModelState.AddModelError(string.Empty, "Neplatný kód. Zkuste to znovu. Po několika chybách se účet na chvíli zamkne.");
        return Page();
    }

    private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Content("~/");

    public sealed class TwoFactorInput
    {
        [Required(ErrorMessage = "Kód je povinný.")]
        [StringLength(12, MinimumLength = 6, ErrorMessage = "Kód má 6 číslic.")]
        [Display(Name = "Kód z aplikace")]
        public string Code { get; set; } = string.Empty;

        [Display(Name = "Pamatovat si toto zařízení")]
        public bool RememberMachine { get; set; }
    }
}
