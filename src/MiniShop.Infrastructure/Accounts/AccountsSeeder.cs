using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Účty krok 2: první admin z konfigurace (jen pro vývoj). V produkci se nenastavuje – admina založí příkaz
/// <c>create-admin</c> přímo na serveru, heslo tak nikdy neleží v GitHubu ani v souboru.
/// </summary>
/// <param name="Email">E-mail (= přihlašovací jméno) prvního admina.</param>
/// <param name="Password">Dočasné heslo – v repozitáři, proto MustChangePassword.</param>
/// <param name="DisplayName">Zobrazované jméno.</param>
/// <param name="MustChangePassword">Po prvním přihlášení vynutit změnu hesla.</param>
public sealed record AdminBootstrapSettings(
    string Email,
    string Password,
    string DisplayName = "Administrátor",
    bool MustChangePassword = true);

/// <summary>
/// Účty krok 2: připraví role a prvního admina. Běží při každém startu Webu – všechno je idempotentní
/// (podruhé neudělá nic).
/// </summary>
public sealed class AccountsSeeder(
    AccountsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ILogger<AccountsSeeder> logger)
{
    public async Task SeedAsync(AdminBootstrapSettings? bootstrap, CancellationToken cancellationToken = default)
    {
        await EnsureRolesAsync();
        await AssignUserRoleToUsersWithoutRoleAsync(cancellationToken);

        if (await AnyAdminAsync())
        {
            return;
        }

        if (bootstrap is null || string.IsNullOrWhiteSpace(bootstrap.Email))
        {
            logger.LogWarning(
                "MiniShop nemá žádného admina. Založte ho příkazem: dotnet MiniShop.Web.dll create-admin --email <e-mail>");
            return;
        }

        var result = await CreateFirstAdminAsync(
            bootstrap.Email, bootstrap.DisplayName, bootstrap.Password, bootstrap.MustChangePassword);
        if (!result.Succeeded)
        {
            logger.LogError("Prvního admina z konfigurace AdminBootstrap se nepodařilo vytvořit: {Errors}",
                string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }

    /// <summary>
    /// Vytvoří prvního admina – jen když žádný admin není. Admina jmenuje admin; tohle je jediná výjimka
    /// (problém slepice a vejce), proto je tak přísná.
    /// </summary>
    /// <remarks>
    /// Existující účet se adminem nikdy nestane: bez potvrzení e-mailu by si kdokoli mohl adminův e-mail
    /// zaregistrovat dřív a seed by mu práva admina dal.
    /// </remarks>
    public async Task<IdentityResult> CreateFirstAdminAsync(
        string email, string displayName, string password, bool mustChangePassword)
    {
        await EnsureRolesAsync();

        if (await AnyAdminAsync())
        {
            return Failed("Admin už existuje. Dalšího admina jmenuje admin v aplikaci.");
        }

        email = email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Failed("Účet s tímto e-mailem už existuje – prvního admina nejde vytvořit povýšením existujícího účtu.");
        }

        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName.Trim() };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return result;
        }

        result = await userManager.AddToRoleAsync(user, AccountRoles.Admin);
        if (result.Succeeded && mustChangePassword)
        {
            result = await userManager.AddClaimAsync(user, new Claim(AccountClaimTypes.MustChangePassword, "true"));
        }

        if (result.Succeeded)
        {
            logger.LogInformation("Vytvořen první admin {UserId}", user.Id);
        }

        return result;
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in AccountRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>Účty z kroku 1 vznikly bez role – dostanou roli User (jednou, pak už žádný takový není).</summary>
    private async Task AssignUserRoleToUsersWithoutRoleAsync(CancellationToken cancellationToken)
    {
        var usersWithoutRole = await dbContext.Users
            .Where(user => !dbContext.UserRoles.Any(userRole => userRole.UserId == user.Id))
            .ToListAsync(cancellationToken);

        foreach (var user in usersWithoutRole)
        {
            await userManager.AddToRoleAsync(user, AccountRoles.User);
            logger.LogInformation("Uživatel {UserId} bez role dostal roli {Role}", user.Id, AccountRoles.User);
        }
    }

    private async Task<bool> AnyAdminAsync() =>
        (await userManager.GetUsersInRoleAsync(AccountRoles.Admin)).Count > 0;

    private static IdentityResult Failed(string description) =>
        IdentityResult.Failed(new IdentityError { Code = "FirstAdmin", Description = description });
}
