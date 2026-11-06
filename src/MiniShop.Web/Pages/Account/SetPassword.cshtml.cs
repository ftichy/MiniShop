using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 10: účet založený přes Google nemá heslo – tady si ho může nastavit (pak se přihlásí i bez Googlu
/// a může Google odpojit). Účet, který heslo má, jde na běžnou změnu hesla.
/// </summary>
[EnableRateLimiting(AccountRateLimiting.PolicyName)]
public sealed class SetPasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<SetPasswordModel> logger) : PageModel
{
    [BindProperty]
    public SetPasswordInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        return await userManager.HasPasswordAsync(user) ? RedirectToPage("./ChangePassword") : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // AddPasswordAsync selže, když účet heslo už má – nastavit jde jen poprvé (jinak změna se současným heslem).
        var result = await userManager.AddPasswordAsync(user, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var description in result.Errors.Select(error => error.Description).Distinct())
            {
                ModelState.AddModelError(string.Empty, description);
            }

            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        logger.LogInformation("Uživatel {UserId} si nastavil heslo", user.Id);
        TempData["StatusMessage"] = "Heslo je nastavené. Přihlásit se teď můžete e-mailem a heslem i přes Google.";
        return RedirectToPage("./ChangePassword");
    }

    public sealed class SetPasswordInput
    {
        [Required(ErrorMessage = "Nové heslo je povinné.")]
        [StringLength(200, MinimumLength = AccountsInfrastructure.PasswordMinLength,
            ErrorMessage = "Heslo musí mít alespoň {2} znaků.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nové heslo")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Hesla se neshodují.")]
        [Display(Name = "Nové heslo znovu")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
