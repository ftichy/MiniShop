using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>Vlastní claims v přihlašovací cookie MiniShopu.</summary>
public static class AccountClaimTypes
{
    /// <summary>Zobrazované jméno uživatele (hlavička stránky) – ať se kvůli němu nemusí číst databáze.</summary>
    public const string DisplayName = "minishop:display_name";

    /// <summary>
    /// Účty krok 2: uživatel musí změnit heslo (první admin se známým heslem). Je to claim uživatele
    /// v tabulce AspNetUserClaims – Identity ho sama přidá do cookie, schéma users.db se nemění.
    /// </summary>
    public const string MustChangePassword = "minishop:must_change_password";
}

/// <summary>
/// Sestaví identitu, která se po přihlášení zašifruje do cookie. Identity sama přidá ID, e-mail, security stamp
/// a role; tady přibude zobrazované jméno.
/// </summary>
public sealed class AccountClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AccountClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}
