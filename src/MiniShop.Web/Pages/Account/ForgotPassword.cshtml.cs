using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 5: zapomenuté heslo – na e-mail přijde odkaz na nastavení nového.
/// Odpověď je vždy stejná, ať účet existuje, nebo ne – nikdo tak nezjistí, kdo má v MiniShopu účet.
/// </summary>
[AllowAnonymous]
public sealed class ForgotPasswordModel(
    UserManager<ApplicationUser> userManager,
    AccountEmails accountEmails,
    ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email.Trim());
        if (user is not null)
        {
            try
            {
                await accountEmails.SendPasswordResetAsync(user, HttpContext, cancellationToken);
                logger.LogInformation("Odkaz pro nové heslo odeslán uživateli {UserId}", user.Id);
            }
            catch (Exception ex)
            {
                // Uživateli stejná odpověď jako vždy (jinak by chyba prozradila, že účet existuje) – jen do logu.
                logger.LogError(ex, "E-mail s odkazem pro nové heslo (uživatel {UserId}) se nepodařilo odeslat", user.Id);
            }
        }

        Sent = true;
        return Page();
    }

    public sealed class ForgotPasswordInput
    {
        [Required(ErrorMessage = "E-mail je povinný."), EmailAddress(ErrorMessage = "Neplatný e-mail.")]
        [Display(Name = "E-mail")]
        public string Email { get; set; } = string.Empty;
    }
}
