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

    /// <summary>Admin bez 2FA: stránka vysvětlí, proč se sem dostal místo do administrace.</summary>
    public bool IsRequiredForAdmin { get; private set; }
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
        // Nová cookie už nese claim „má 2FA“ – bez toho by admin po zapnutí pořád narážel na zámek administrace.
        await signInManager.RefreshSignInAsync(user);
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

    /// <summary>
    /// Nový telefon / smazaná aplikace: přihlášený uživatel prokáže vlastnictví účtu kódem z aplikace NEBO záchranným
    /// kódem (spotřebuje se) – samotná přihlášená relace nestačí. Starý klíč pak přestane platit, 2FA se vypne
    /// a stránka ukáže nový QR kód; zapnutím (první kód z nové aplikace) vzniknou nové záchranné kódy.
    /// Platí i pro admina: mezitím ho policy RequireAdmin pustí jen sem (bez claimu two_factor nic nespravuje).
    /// </summary>
    public async Task<IActionResult> OnPostNewAppAsync()
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

        var proven = await IsValidCodeAsync(user)
                     || (await userManager.RedeemTwoFactorRecoveryCodeAsync(
                         user, TwoFactorHelper.NormalizeRecoveryCode(Code))).Succeeded;
        if (!proven)
        {
            // Hádání kódů se počítá do zámku účtu (projeví se při dalším přihlášení), stejně jako při přihlášení.
            await userManager.AccessFailedAsync(user);
            logger.LogWarning("Neplatný kód při výměně aplikace 2FA u účtu {UserId}", user.Id);
            ModelState.AddModelError(string.Empty, "Kód nesouhlasí – aplikace zůstává beze změny.");
            await LoadAsync(user);
            return Page();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);   // starý klíč přestane platit (i nový security stamp)
        await signInManager.RefreshSignInAsync(user);
        await signInManager.ForgetTwoFactorClientAsync();
        logger.LogInformation("Uživatel {UserId} vyměnil aplikaci pro dvoufázové ověření", user.Id);

        StatusMessage = "Starý klíč už neplatí. Naskenujte nový QR kód a zapněte dvoufázové ověření prvním kódem z aplikace.";
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
        var roles = await userManager.GetRolesAsync(user);
        CanDisable = TwoFactorHelper.MayDisable(roles);
        IsRequiredForAdmin = !IsEnabled && !TwoFactorHelper.MayDisable(roles);

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
