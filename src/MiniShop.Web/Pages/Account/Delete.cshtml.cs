using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Exceptions;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 9: uživatel si smaže účet (GDPR – právo být zapomenut). Nejdřív znovu zadá heslo (kdo jen našel
/// otevřený prohlížeč, účet nesmaže), pak Web anonymizuje jeho objednávky (Ordering) a smaže účet (Accounts).
/// Účty krok 10: účet bez hesla (založený přes Google) to potvrdí novým přihlášením u Googlu.
/// </summary>
/// <remarks>
/// Moduly se neznají – spojí je až tahle stránka. Pořadí: objednávky, pak účet (viz <see cref="AccountDeletion"/>).
/// </remarks>
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // ověřuje heslo – stejný limit jako přihlášení
public sealed class DeleteModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AccountDeletion accountDeletion,
    IOrderService orderService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Pro smazání účtu zadejte heslo.")]
    [DataType(DataType.Password)]
    [Display(Name = "Heslo")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Proč účet smazat nejde (admin) – formulář se pak neukáže.</summary>
    public string? CannotDeleteReason { get; private set; }

    /// <summary>Účty krok 10: účet bez hesla potvrdí smazání přihlášením u Googlu (místo pole Heslo).</summary>
    public bool ConfirmWithGoogle { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        if (CannotDeleteReason is not null || ConfirmWithGoogle || !ModelState.IsValid)
        {
            return Page();
        }

        // Heslo se počítá do zámku účtu (krok 8) – hádat ho tu nejde o nic lépe než při přihlášení.
        var check = await signInManager.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            ModelState.AddModelError(nameof(Password), "Heslo nesouhlasí.");
            return Page();
        }

        return await DeleteAsync(user, cancellationToken);
    }

    /// <summary>Účty krok 10: na Google s <c>prompt=login</c> – Google se zeptá na přihlášení znovu.</summary>
    public async Task<IActionResult> OnPostGoogleAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        ModelState.Clear();   // formulář posílá jen antiforgery token – pole Heslo tu nepatří
        if (CannotDeleteReason is not null || !ConfirmWithGoogle)
        {
            return Page();
        }

        // ID účtu ve „state“: návrat z Googlu platí jen pro tento přihlášený účet.
        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            GoogleLogin.Scheme, Url.Page("./Delete", "GoogleConfirmed"), user.Id);
        GoogleLogin.RequireFreshLogin(properties);
        return Challenge(properties, GoogleLogin.Scheme);
    }

    /// <summary>
    /// Návrat z Googlu. Smaže se jen tehdy, když se uživatel přihlásil právě tím účtem Google, který je
    /// s tímto účtem MiniShopu propojený (dočasná cookie platí 5 minut a hned se maže).
    /// </summary>
    public async Task<IActionResult> OnGetGoogleConfirmedAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var info = await signInManager.GetExternalLoginInfoAsync(user.Id);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        await LoadAsync(user);
        if (CannotDeleteReason is not null)
        {
            return Page();
        }

        var owner = info is null ? null : await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (owner?.Id != user.Id)
        {
            ModelState.AddModelError(string.Empty,
                "Ověření přes Google se nepovedlo – přihlaste se účtem Google, který je s MiniShopem propojený.");
            return Page();
        }

        return await DeleteAsync(user, cancellationToken);
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        CannotDeleteReason = await accountDeletion.CanDeleteAsync(user);
        ConfirmWithGoogle = !await userManager.HasPasswordAsync(user)
            && (await userManager.GetLoginsAsync(user)).Any(login => login.LoginProvider == GoogleLogin.Scheme);
    }

    private async Task<IActionResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        try
        {
            await orderService.AnonymizeCustomerAsync(user.Id, cancellationToken);
        }
        catch (DomainException ex)
        {
            // Nevyřízené objednávky – nic se nezměnilo.
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        // Propojení s Googlem (AspNetUserLogins) smaže databáze spolu s účtem (kaskáda).
        var result = await accountDeletion.DeleteAsync(user, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        await signInManager.SignOutAsync();
        StatusMessage = "Účet je smazaný. Objednávky zůstaly bez vašeho jména a e-mailu. Děkujeme, že jste nakupovali v MiniShopu.";
        return RedirectToPage("/Index");
    }
}
