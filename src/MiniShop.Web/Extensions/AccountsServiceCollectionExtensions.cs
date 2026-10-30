using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Extensions;

/// <summary>
/// Účty krok 1: přihlašování do Webu. Jádro Identity (uživatelé, hesla, users.db) dodá Infrastructure,
/// tady přibude SignInManager a přihlašovací cookie. Krok 2: role a pravidla, kdo smí co (policies).
/// </summary>
public static class AccountsServiceCollectionExtensions
{
    public static IServiceCollection AddAccounts(this IServiceCollection services, string connectionString)
    {
        services.AddAccountsInfrastructure(connectionString)
            .AddSignInManager()
            .AddDefaultTokenProviders();   // krok 5: tokeny do odkazů v e-mailu (potvrzení, reset hesla)

        // Krok 5: odkaz z e-mailu platí 2 hodiny. Token je podepsaný klíči Data Protection a obsahuje security stamp –
        // po změně hesla přestane platit (reset jde použít jen jednou).
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(2));
        services.AddScoped<AccountEmails>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.RequireAdmin, policy => policy.RequireRole(AccountRoles.Admin))
            .AddPolicy(AuthorizationPolicies.RequireCustomer, policy => policy.RequireRole(AccountRoles.User))
            // Secure by default: co není výslovně [AllowAnonymous], to vyžaduje přihlášení.
            // Na novou stránku se tak nedá „zapomenout“ – bez zámku by ji viděl každý.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = ".MiniShop.Auth";
            options.Cookie.HttpOnly = true;                          // JavaScript na stránce cookie nepřečte
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // jen přes HTTPS (localhost prohlížeče berou jako bezpečný)
            options.Cookie.SameSite = SameSiteMode.Lax;              // z cizích webů se neposílá s POST (ochrana proti CSRF)

            // „Zapamatovat si mě“ = 14 dní; každé použití v druhé polovině doby platnost prodlouží.
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";

            // REST API nemá kam přesměrovat – volající (skript, Postman) dostane 401/403, ne HTML přihlašovací stránky.
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => RespondOrRedirect(context, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => RespondOrRedirect(context, StatusCodes.Status403Forbidden)
            };
        });

        return services;
    }

    private static Task RespondOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int apiStatusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = apiStatusCode;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}
