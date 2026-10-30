using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 5: nové heslo podle odkazu z e-mailu. Token platí 2 hodiny a jen jednou – změna hesla změní
/// security stamp, se kterým je token podepsaný (a zároveň odhlásí ostatní zařízení).
/// </summary>
[AllowAnonymous]
public sealed class ResetPasswordModel(
    UserManager<ApplicationUser> userManager,
    ILogger<ResetPasswordModel> logger) : PageModel
{
    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new();

    public bool Done { get; private set; }

    public bool InvalidLink { get; private set; }

    public void OnGet(string? email, string? code)
    {
        InvalidLink = string.IsNullOrEmpty(email) || AccountEmails.DecodeToken(code) is null;
        Input.Email = email ?? string.Empty;
        Input.Code = code ?? string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var token = AccountEmails.DecodeToken(Input.Code);
        var user = await userManager.FindByEmailAsync(Input.Email);
        if (token is null || user is null)
        {
            // Neexistující účet = stejná hláška jako neplatný odkaz (nic neprozradit).
            ModelState.AddModelError(string.Empty, "Odkaz je neplatný, už byl použitý nebo vypršel. Požádejte o nový.");
            return Page();
        }

        var result = await userManager.ResetPasswordAsync(user, token, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var description in result.Errors.Select(error => error.Description).Distinct())
            {
                ModelState.AddModelError(string.Empty, description);
            }

            return Page();
        }

        // Heslo si teď zvolil sám – dočasné heslo (krok 2) už neplatí.
        var mustChange = (await userManager.GetClaimsAsync(user))
            .Where(claim => claim.Type == AccountClaimTypes.MustChangePassword)
            .ToList();
        if (mustChange.Count > 0)
        {
            await userManager.RemoveClaimsAsync(user, mustChange);
        }

        logger.LogInformation("Uživatel {UserId} si nastavil nové heslo přes odkaz z e-mailu", user.Id);
        Done = true;
        return Page();
    }

    public sealed class ResetPasswordInput
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;

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
