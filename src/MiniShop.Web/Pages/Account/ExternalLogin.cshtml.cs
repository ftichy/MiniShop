using Microsoft.AspNetCore.Authentication;
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
/// Účty krok 10: přihlášení přes Google. POST pošle prohlížeč na Google (Challenge), Google ho vrátí na
/// <c>/signin-google</c> (to zpracuje ASP.NET Core sám) a odtud sem na <see cref="OnGetCallbackAsync"/> – tady se
/// vybere účet MiniShopu (<see cref="ExternalAccountLinker"/>) a vydá běžná přihlašovací cookie.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // limit jen na POST (start přihlášení), jako u ostatních formulářů účtu
public sealed class ExternalLoginModel(
    SignInManager<ApplicationUser> signInManager,
    ExternalAccountLinker linker,
    ILogger<ExternalLoginModel> logger) : PageModel
{
    /// <summary>Proč přihlášení nevyšlo – stránka ho ukáže s odkazem zpět na přihlášení.</summary>
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => RedirectToPage("./Login");

    public async Task<IActionResult> OnPostAsync(string provider)
    {
        // Jen nastavení poskytovatelé – jinak by šlo podstrčit libovolné schéma (třeba cookie).
        if ((await signInManager.GetExternalAuthenticationSchemesAsync()).All(scheme => scheme.Name != provider))
        {
            return NotFound();
        }

        // ConfigureExternalAuthenticationProperties uloží jméno poskytovatele; ochranu proti podvrženému návratu
        // (parametr state + korelační cookie) přidá handler Googlu sám.
        var redirectUrl = Url.Page("./ExternalLogin", "Callback", new { ReturnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? remoteError)
    {
        if (remoteError is not null)
        {
            // Uživatel na Googlu klikl na Zrušit, nebo Google odmítl požadavek.
            logger.LogWarning("Přihlášení přes Google vrátilo chybu: {RemoteError}", remoteError);
            return Error("Přihlášení přes Google se nepovedlo nebo bylo zrušeno.");
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            // Dočasná cookie s identitou chybí nebo vypršela (5 minut).
            return Error("Přihlášení přes Google vypršelo. Zkuste to prosím znovu.");
        }

        // Propojený účet: přihlásit. SignInManager zkontroluje zámek a blokaci adminem stejně jako u hesla.
        var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey,
            isPersistent: false, bypassTwoFactor: false);

        // Google se jako druhý faktor nepočítá: kdo má zapnuté 2FA (admin vždy), zadá po Googlu ještě kód z aplikace.
        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl });
        }

        if (!result.Succeeded && !result.IsLockedOut)
        {
            var linked = await linker.LinkOrCreateAsync(info.ToExternalAccount());
            switch (linked.Outcome)
            {
                case ExternalAccountOutcome.EmailNotVerified:
                    await SignOutExternalAsync();
                    return Error("Google nepotvrdil, že vám e-mail účtu patří. Přihlaste se e-mailem a heslem.");
                case ExternalAccountOutcome.ExistingAccountNotConfirmed:
                    await SignOutExternalAsync();
                    return Error("Účet s tímto e-mailem už v MiniShopu existuje, ale e-mail zatím není potvrzený. " +
                        "Přihlaste se heslem a potvrďte e-mail (odkaz v e-mailu) – pak se půjde přihlásit i přes Google.");
                case ExternalAccountOutcome.Failed:
                    await SignOutExternalAsync();
                    return Error("Účet se nepodařilo propojit: " + string.Join(" ", linked.Errors ?? []));
            }

            result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey,
                isPersistent: false, bypassTwoFactor: false);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl });
        }

        if (result.Succeeded)
        {
            // Dočasnou cookie SignInManager po úspěchu smaže sám.
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Content("~/"));
        }

        await SignOutExternalAsync();
        // Stejná hláška pro zamčený i zablokovaný účet jako u přihlášení heslem.
        return Error(result.IsLockedOut
            ? "Účet je zamčený nebo zablokovaný. Zkuste to později, případně kontaktujte správce."
            : "Přihlášení přes Google se nepovedlo.");
    }

    private Task SignOutExternalAsync() => HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

    private PageResult Error(string message)
    {
        ErrorMessage = message;
        return Page();
    }
}
