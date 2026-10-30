using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Infrastruktura modulu Accounts (účty krok 1): databáze users.db a jádro ASP.NET Core Identity
/// (UserManager, hashování hesel, pravidla hesla, role).
/// </summary>
/// <remarks>
/// Vrací IdentityBuilder: přihlašování přes cookie (SignInManager) je věc webu a přidá ho Web.
/// Testy tak pracují se stejnými pravidly jako aplikace, jen bez HTTP.
/// </remarks>
public static class AccountsInfrastructure
{
    public const int PasswordMinLength = 10;

    public static IdentityBuilder AddAccountsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccountsDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<AccountsSeeder>();
        services.AddScoped<AccountAdministration>();   // účty krok 6: správa uživatelů adminem
        services.TryAddSingleton(TimeProvider.System);

        return services.AddIdentityCore<ApplicationUser>(ConfigureIdentity)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AccountsDbContext>()
            .AddClaimsPrincipalFactory<AccountClaimsPrincipalFactory>()
            .AddErrorDescriber<CzechIdentityErrorDescriber>();
    }

    private static void ConfigureIdentity(IdentityOptions options)
    {
        // Pravidla hesla podle NIST: rozhoduje délka, ne povinné velké písmeno nebo speciální znak
        // (ty vedou k heslům typu "Heslo123!", která se snadno uhodnou).
        options.Password.RequiredLength = PasswordMinLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // Přihlašovací jméno je e-mail – a jeden e-mail = jeden účet.
        options.User.RequireUniqueEmail = true;

        // Potvrzení e-mailu přijde s odesíláním e-mailů (účty krok 5), zámek účtu se zpevněním (krok 8).
        options.SignIn.RequireConfirmedAccount = false;
    }
}
