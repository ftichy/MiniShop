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

/// <summary>Účty krok 1: registrace nového uživatele. Po úspěchu je rovnou přihlášený.</summary>
[AllowAnonymous]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // účty krok 8: max. 10 odeslání za minutu z jedné IP
public sealed class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AccountEmails accountEmails,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    /// <summary>Kam se vrátit po registraci (např. stránka, která přihlášení vyžadovala).</summary>
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

        var email = Input.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = Input.DisplayName.Trim()
        };

        // Identity zkontroluje pravidla hesla a unikátní e-mail, heslo uloží jen jako hash se solí.
        var result = await userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            // Duplicitní jméno i e-mail mají stejnou hlášku (přihlašovací jméno = e-mail) – stačí jednou.
            foreach (var description in result.Errors.Select(error => error.Description).Distinct())
            {
                ModelState.AddModelError(string.Empty, description);
            }

            return Page();
        }

        // Účty krok 2: každý nový účet je zákazník. (Kdyby přiřazení selhalo, roli doplní seed při dalším startu.)
        await userManager.AddToRoleAsync(user, AccountRoles.User);

        // Do logu jen ID – e-mail je osobní údaj.
        logger.LogInformation("Zaregistrován nový uživatel {UserId}", user.Id);

        // Účty krok 5: odkaz pro potvrzení e-mailu. Když se nepovede odeslat, účet platí dál – upozornění na stránce
        // nabídne „Poslat znovu“.
        try
        {
            await accountEmails.SendConfirmationAsync(user, HttpContext, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Potvrzovací e-mail pro nového uživatele {UserId} se nepodařilo odeslat", user.Id);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(SafeReturnUrl());
    }

    /// <summary>Jen adresa v rámci MiniShopu – jinak by šlo po registraci přesměrovat na podvodný web (open redirect).</summary>
    private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Content("~/");

    public sealed class RegisterInput
    {
        [Required(ErrorMessage = "Jméno je povinné."), StringLength(ApplicationUser.DisplayNameMaxLength)]
        [Display(Name = "Jméno")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-mail je povinný."), EmailAddress(ErrorMessage = "Neplatný e-mail.")]
        [Display(Name = "E-mail")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Heslo je povinné.")]
        [StringLength(200, MinimumLength = AccountsInfrastructure.PasswordMinLength,
            ErrorMessage = "Heslo musí mít alespoň {2} znaků.")]
        [DataType(DataType.Password)]
        [Display(Name = "Heslo")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Hesla se neshodují.")]
        [Display(Name = "Heslo znovu")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
