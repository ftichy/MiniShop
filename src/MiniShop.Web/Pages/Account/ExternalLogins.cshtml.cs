using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 10: propojení účtu MiniShopu s Googlem z nastavení účtu – připojit (i k účtu s nepotvrzeným e-mailem,
/// uživatel je přihlášený heslem, takže je to jeho účet) a odpojit (jen když se dál přihlásí jinak).
/// </summary>
[EnableRateLimiting(AccountRateLimiting.PolicyName)]
public sealed class ExternalLoginsModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ExternalAccountLinker linker) : PageModel
{
    public IReadOnlyList<UserLoginInfo> Logins { get; private set; } = [];

    /// <summary>Nastavení poskytovatelé, se kterými účet ještě propojený není.</summary>
    public IReadOnlyList<AuthenticationScheme> AvailableSchemes { get; private set; } = [];

    public bool HasPassword { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        Logins = (await userManager.GetLoginsAsync(user)).ToList();
        HasPassword = await userManager.HasPasswordAsync(user);
        AvailableSchemes = (await signInManager.GetExternalAuthenticationSchemesAsync())
            .Where(scheme => Logins.All(login => login.LoginProvider != scheme.Name))
            .ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostLinkAsync(string provider)
    {
        if ((await signInManager.GetExternalAuthenticationSchemesAsync()).All(scheme => scheme.Name != provider))
        {
            return NotFound();
        }

        // Třetí parametr (ID účtu) se uloží do „state“ – návrat z Googlu pak patří jen k tomuto přihlášenému účtu.
        var redirectUrl = Url.Page("./ExternalLogins", "LinkCallback");
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, userManager.GetUserId(User));
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> OnGetLinkCallbackAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var info = await signInManager.GetExternalLoginInfoAsync(user.Id);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (info is null)
        {
            ErrorMessage = "Propojení se nepovedlo nebo vypršelo. Zkuste to prosím znovu.";
            return RedirectToPage();
        }

        var result = await linker.LinkAsync(user, info.ToExternalAccount());
        if (result.Succeeded)
        {
            StatusMessage = $"Účet je propojený s {info.ProviderDisplayName}. Přihlásit se teď můžete i přes něj.";
        }
        else
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(error => error.Description));
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnlinkAsync(string provider, string providerKey)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await linker.UnlinkAsync(user, provider, providerKey);
        if (result.Succeeded)
        {
            // Nový security stamp – obnovit vlastní cookie, ať tohle zařízení zůstane přihlášené.
            await signInManager.RefreshSignInAsync(user);
            StatusMessage = "Propojení je zrušené.";
        }
        else
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(error => error.Description));
        }

        return RedirectToPage();
    }
}
