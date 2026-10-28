using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Pages.Account;

/// <summary>Účty krok 1: přihlášení e-mailem a heslem. Výsledek je zašifrovaná cookie s identitou.</summary>
public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
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

        // lockoutOnFailure: false – zámek účtu po opakovaném špatném hesle přijde se zpevněním (účty krok 8).
        var result = await signInManager.PasswordSignInAsync(
            Input.Email.Trim(), Input.Password, Input.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            return LocalRedirect(SafeReturnUrl());
        }

        // Stejná hláška pro neznámý e-mail i špatné heslo – nikdo tak nezjistí, kdo v MiniShopu má účet.
        logger.LogWarning("Neúspěšné přihlášení");
        ModelState.AddModelError(string.Empty, "Neplatný e-mail nebo heslo.");
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
