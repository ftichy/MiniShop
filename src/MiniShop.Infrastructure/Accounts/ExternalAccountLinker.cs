using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Účty krok 10: identita, kterou vrátil vnější poskytovatel (Google) – stálé ID účtu u poskytovatele, e-mail a jméno.
/// </summary>
/// <param name="Provider">Jméno schématu („Google“) – v tabulce AspNetUserLogins sloupec LoginProvider.</param>
/// <param name="ProviderKey">Stálé ID účtu u poskytovatele (u Googlu <c>sub</c>) – e-mail se může změnit, tohle ne.</param>
/// <param name="EmailVerified">Poskytovatel ověřil, že e-mail patří tomuto účtu.</param>
public sealed record ExternalAccount(
    string Provider,
    string ProviderKey,
    string ProviderDisplayName,
    string? Email,
    bool EmailVerified,
    string? DisplayName);

public enum ExternalAccountOutcome
{
    /// <summary>Účet MiniShopu už byl propojený, nebo se právě propojil (stejný potvrzený e-mail).</summary>
    Linked,

    /// <summary>Nový účet MiniShopu bez hesla (role User, e-mail potvrzený poskytovatelem).</summary>
    Created,

    /// <summary>Poskytovatel e-mail nevrátil nebo ho neověřil – podle něj účet nepárujeme ani nezakládáme.</summary>
    EmailNotVerified,

    /// <summary>Účet se stejným e-mailem existuje, ale e-mail nemá potvrzený – automaticky propojit nejde.</summary>
    ExistingAccountNotConfirmed,

    /// <summary>Identity účet nezaložila nebo nepropojila (viz chyby).</summary>
    Failed
}

public sealed record ExternalAccountResult(
    ExternalAccountOutcome Outcome,
    ApplicationUser? User = null,
    IReadOnlyList<string>? Errors = null);

/// <summary>
/// Účty krok 10: pravidla přihlášení přes Google – kdy účet propojit, kdy založit a kdy odmítnout. Přihlášení samo
/// (cookie, kontrola zámku a blokace) dělá Web přes SignInManager, tady je jen rozhodnutí nad databází účtů.
/// </summary>
/// <remarks>
/// Proč se nepotvrzený účet automaticky nepropojí (<i>pre-account hijacking</i>): útočník by si mohl založit účet
/// s cizím e-mailem a vlastním heslem. Až by se majitel e-mailu přihlásil Googlem, účty by se spojily a útočník by
/// znal heslo k cizímu účtu. Potvrzený e-mail dokazuje, že účet MiniShopu založil majitel schránky.
/// </remarks>
public sealed class ExternalAccountLinker(UserManager<ApplicationUser> userManager, ILogger<ExternalAccountLinker> logger)
{
    /// <summary>Přihlášení přes poskytovatele: najít propojený účet, propojit podle e-mailu, nebo založit nový.</summary>
    public async Task<ExternalAccountResult> LinkOrCreateAsync(ExternalAccount account)
    {
        var linked = await userManager.FindByLoginAsync(account.Provider, account.ProviderKey);
        if (linked is not null)
        {
            return new(ExternalAccountOutcome.Linked, linked);
        }

        if (!account.EmailVerified || string.IsNullOrWhiteSpace(account.Email))
        {
            logger.LogWarning("Přihlášení přes {Provider} odmítnuto – poskytovatel neověřil e-mail", account.Provider);
            return new(ExternalAccountOutcome.EmailNotVerified);
        }

        var email = account.Email.Trim();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (!existing.EmailConfirmed)
            {
                logger.LogWarning("Přihlášení přes {Provider} k účtu {UserId} odmítnuto – e-mail účtu není potvrzený",
                    account.Provider, existing.Id);
                return new(ExternalAccountOutcome.ExistingAccountNotConfirmed);
            }

            var addLogin = await userManager.AddLoginAsync(existing, ToLoginInfo(account));
            if (!addLogin.Succeeded)
            {
                return Failed(addLogin);
            }

            logger.LogInformation("Účet {UserId} propojen s {Provider} (stejný potvrzený e-mail)", existing.Id, account.Provider);
            return new(ExternalAccountOutcome.Linked, existing);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,   // ověřil ho poskytovatel – potvrzovací e-mail by byl zbytečný
            DisplayName = DisplayNameFor(account, email)
        };

        // Bez hesla: přihlašuje se jen přes poskytovatele. Heslo si může nastavit později (stránka Nastavit heslo).
        var create = await userManager.CreateAsync(user);
        if (!create.Succeeded)
        {
            return Failed(create);
        }

        await userManager.AddToRoleAsync(user, AccountRoles.User);
        var login = await userManager.AddLoginAsync(user, ToLoginInfo(account));
        if (!login.Succeeded)
        {
            // Účet bez propojení: při dalším pokusu se propojí podle (potvrzeného) e-mailu.
            return Failed(login);
        }

        logger.LogInformation("Zaregistrován nový uživatel {UserId} přes {Provider}", user.Id, account.Provider);
        return new(ExternalAccountOutcome.Created, user);
    }

    /// <summary>Propojení z nastavení účtu – přihlášený uživatel připojí svůj účet u poskytovatele.</summary>
    public async Task<IdentityResult> LinkAsync(ApplicationUser user, ExternalAccount account)
    {
        var owner = await userManager.FindByLoginAsync(account.Provider, account.ProviderKey);
        if (owner is not null)
        {
            return owner.Id == user.Id
                ? IdentityResult.Success
                : IdentityResult.Failed(new IdentityError
                {
                    Code = "LoginAlreadyAssociated",
                    Description = $"Tento účet {account.ProviderDisplayName} už je propojený s jiným účtem MiniShopu."
                });
        }

        var result = await userManager.AddLoginAsync(user, ToLoginInfo(account));
        if (result.Succeeded)
        {
            logger.LogInformation("Účet {UserId} propojen s {Provider}", user.Id, account.Provider);
        }

        return result;
    }

    /// <summary>Zrušení propojení – jen když se uživatel dál přihlásí jinak (heslem nebo jiným propojením).</summary>
    public async Task<IdentityResult> UnlinkAsync(ApplicationUser user, string provider, string providerKey)
    {
        var logins = await userManager.GetLoginsAsync(user);
        if (!await userManager.HasPasswordAsync(user) && logins.Count <= 1)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "LastLogin",
                Description = "Nejdřív si nastavte heslo – jinak byste se k účtu už nepřihlásili."
            });
        }

        // RemoveLoginAsync změní security stamp – přihlášení na jiných zařízeních se do minuty ověří znovu.
        var result = await userManager.RemoveLoginAsync(user, provider, providerKey);
        if (result.Succeeded)
        {
            logger.LogInformation("Účet {UserId} odpojen od {Provider}", user.Id, provider);
        }

        return result;
    }

    private static UserLoginInfo ToLoginInfo(ExternalAccount account) =>
        new(account.Provider, account.ProviderKey, account.ProviderDisplayName);

    private static string DisplayNameFor(ExternalAccount account, string email)
    {
        var name = string.IsNullOrWhiteSpace(account.DisplayName) ? email.Split('@')[0] : account.DisplayName.Trim();
        return name.Length <= ApplicationUser.DisplayNameMaxLength ? name : name[..ApplicationUser.DisplayNameMaxLength];
    }

    private static ExternalAccountResult Failed(IdentityResult result) =>
        new(ExternalAccountOutcome.Failed, Errors: result.Errors.Select(error => error.Description).Distinct().ToList());
}
