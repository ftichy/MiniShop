using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Dvoufázové ověření (TOTP, např. Google/Microsoft Authenticator): zapnutí (tajný klíč + QR kód + první kód),
/// záchranné kódy a vypnutí. Jako přirovnání: heslo je klíč od dveří, kód z telefonu je druhý zámek,
/// který se každých 30 sekund mění – s ukradeným heslem se bez telefonu dovnitř nedostanete.
/// Přístupné každému přihlášenému (fallback policy). Do logu jen ID účtu, nikdy kód ani klíč.
/// </summary>
[EnableRateLimiting(AccountRateLimiting.PolicyName)]
public sealed class TwoFactorModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<TwoFactorModel> logger) : PageModel
{
    /// <summary>Kód z aplikace – ověřuje se při zapnutí, vypnutí i vytvoření nových záchranných kódů.</summary>
    [BindProperty]
    public string? Code { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public bool IsEnabled { get; private set; }
    public bool CanDisable { get; private set; }
    public int RecoveryCodesLeft { get; private set; }
    public string? SharedKey { get; private set; }
    public string? QrSvg { get; private set; }

    /// <summary>Záchranné kódy – zobrazí se jen v odpovědi na zapnutí / vygenerování, nikam se neukládají znovu.</summary>
    public IReadOnlyList<string>? RecoveryCodes { get; private set; }

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

    /// <summary>Zapnout: kód z aplikace dokazuje, že si uživatel klíč opravdu přidal (jinak by se mohl zamknout).</summary>
    public async Task<IActionResult> OnPostEnableAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (user.TwoFactorEnabled)
        {
            return RedirectToPage();
        }

        if (!await IsValidCodeAsync(user))
        {
            ModelState.AddModelError(string.Empty, "Kód nesouhlasí. Zkontrolujte, že jste v aplikaci přidali tento klíč, a opište aktuální 6místný kód.");
            await LoadAsync(user);
            return Page();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorHelper.RecoveryCodeCount))?.ToList();
        logger.LogInformation("Uživatel {UserId} zapnul dvoufázové ověření", user.Id);

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostRegenerateAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!user.TwoFactorEnabled)
        {
            return RedirectToPage();
        }

        if (!await IsValidCodeAsync(user))
        {
            ModelState.AddModelError(string.Empty, "Kód nesouhlasí – nové záchranné kódy nebyly vytvořeny.");
            await LoadAsync(user);
            return Page();
        }

        RecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorHelper.RecoveryCodeCount))?.ToList();
        logger.LogInformation("Uživatel {UserId} vytvořil nové záchranné kódy", user.Id);

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!user.TwoFactorEnabled)
        {
            return RedirectToPage();
        }

        // Admin si 2FA vypnout nesmí – uniklé heslo admina by pak stačilo k převzetí e-shopu.
        if (!TwoFactorHelper.MayDisable(await userManager.GetRolesAsync(user)))
        {
            return Forbid();
        }

        if (!await IsValidCodeAsync(user))
        {
            ModelState.AddModelError(string.Empty, "Kód nesouhlasí – dvoufázové ověření zůstává zapnuté.");
            await LoadAsync(user);
            return Page();
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);   // starý klíč přestane platit (i nový security stamp)
        await signInManager.RefreshSignInAsync(user);
        // Zapamatovaná zařízení by jinak po novém zapnutí přeskočila kód.
        await signInManager.ForgetTwoFactorClientAsync();
        logger.LogInformation("Uživatel {UserId} vypnul dvoufázové ověření", user.Id);

        StatusMessage = "Dvoufázové ověření je vypnuté.";
        return RedirectToPage();
    }

    /// <summary>Platný 6místný kód z aplikace (záchranné kódy se tu nepřijímají – jen při přihlášení).</summary>
    private async Task<bool> IsValidCodeAsync(ApplicationUser user)
    {
        var code = TwoFactorHelper.NormalizeCode(Code);
        if (!TwoFactorHelper.SixDigits().IsMatch(code))
        {
            return false;
        }

        return await userManager.VerifyTwoFactorTokenAsync(
            user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        IsEnabled = user.TwoFactorEnabled;
        CanDisable = TwoFactorHelper.MayDisable(await userManager.GetRolesAsync(user));

        if (IsEnabled)
        {
            RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
            return;
        }

        // Klíč se vytvoří při prvním zobrazení a zůstává, dokud se 2FA nezapne (obnovení stránky QR nezmění).
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        SharedKey = TwoFactorHelper.FormatKey(key!);
        QrSvg = TwoFactorHelper.QrCodeSvg(TwoFactorHelper.AuthenticatorUri(user.Email ?? user.UserName ?? "uzivatel", key!));
    }
}
