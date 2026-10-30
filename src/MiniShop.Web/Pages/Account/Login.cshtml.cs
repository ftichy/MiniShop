using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>Účty krok 1: přihlášení e-mailem a heslem. Výsledek je zašifrovaná cookie s identitou.</summary>
[AllowAnonymous]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // účty krok 8: max. 10 odeslání za minutu z jedné IP
public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    /// <summary>Kam se vrátit po přihlášení (cookie middleware sem pošle stránku, která přihlášení vyžadovala).</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl()) : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Účty krok 8: lockoutOnFailure – každé špatné heslo se počítá, po 5 se účet na 15 minut zamkne
        // (úspěšné přihlášení počítadlo vynuluje).
        var email = Input.Email.Trim();
        var result = await signInManager.PasswordSignInAsync(email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(SafeReturnUrl());
        }

        // Stejná hláška pro neznámý e-mail, špatné heslo i zamčený/zablokovaný účet (Identity zámek kontroluje
        // ještě před heslem) – jiná by komukoli prozradila, kdo v MiniShopu má účet. Admin stav vidí v přehledu uživatelů.
        // Krok 8: audit do logu – jen ID účtu, nikdy e-mail ani heslo.
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            logger.LogWarning("Neúspěšné přihlášení – neznámý e-mail");
        }
        else if (result.IsLockedOut)
        {
            logger.LogWarning("Přihlášení k zamčenému účtu {UserId}", user.Id);
        }
        else
        {
            logger.LogWarning("Neúspěšné přihlášení k účtu {UserId} (špatné heslo, {FailedCount}. pokus)",
                user.Id, await userManager.GetAccessFailedCountAsync(user));
        }

        ModelState.AddModelError(string.Empty,
            $"Neplatný e-mail nebo heslo. Po {AccountsInfrastructure.MaxFailedAccessAttempts} neúspěšných pokusech " +
            $"se účet na {AccountsInfrastructure.LockoutDuration.TotalMinutes:0} minut zamkne.");
        return Page();
    }

    /// <summary>Jen adresa v rámci MiniShopu – jinak by šlo po přihlášení přesměrovat na podvodný web (open redirect).</summary>
    private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Content("~/");

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "E-mail je povinný."), EmailAddress(ErrorMessage = "Neplatný e-mail.")]
        [Display(Name = "E-mail")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Heslo je povinné.")]
        [DataType(DataType.Password)]
        [Display(Name = "Heslo")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Zapamatovat si mě (14 dní)")]
        public bool RememberMe { get; set; }
    }
}
