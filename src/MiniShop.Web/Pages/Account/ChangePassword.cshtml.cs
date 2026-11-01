using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 2: změna hesla přihlášeného uživatele. Povinná pro admina s dočasným heslem
/// (příznak MustChangePassword – ten ho sem přesměrovává z každé stránky).
/// </summary>
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // účty krok 8: max. 10 odeslání za minutu z jedné IP
public sealed class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public bool MustChangePassword => User.HasClaim(claim => claim.Type == AccountClaimTypes.MustChangePassword);

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        // Ověří současné heslo, zkontroluje pravidla nového a změní security stamp – staré cookie na jiných
        // zařízeních tím přestanou platit (do 30 minut, při další kontrole stampu).
        var result = await userManager.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var description in result.Errors.Select(error => error.Description).Distinct())
            {
                ModelState.AddModelError(string.Empty, description);
            }

            return Page();
        }

        var mustChangeClaims = (await userManager.GetClaimsAsync(user))
            .Where(claim => claim.Type == AccountClaimTypes.MustChangePassword)
            .ToList();
        if (mustChangeClaims.Count > 0)
        {
            await userManager.RemoveClaimsAsync(user, mustChangeClaims);
        }

        // Nová cookie – bez příznaku „musí změnit heslo“ a s novým security stampem.
        await signInManager.RefreshSignInAsync(user);
        logger.LogInformation("Uživatel {UserId} změnil heslo", user.Id);

        StatusMessage = "Heslo je změněné.";
        return RedirectToPage();
    }

    public sealed class ChangePasswordInput
    {
        [Required(ErrorMessage = "Současné heslo je povinné.")]
        [DataType(DataType.Password)]
        [Display(Name = "Současné heslo")]
        public string CurrentPassword { get; set; } = string.Empty;

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
