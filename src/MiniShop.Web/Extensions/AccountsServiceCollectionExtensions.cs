using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Extensions;

/// <summary>
/// Účty krok 1: přihlašování do Webu. Jádro Identity (uživatelé, hesla, users.db) dodá Infrastructure,
/// tady přibude SignInManager a přihlašovací cookie.
/// </summary>
public static class AccountsServiceCollectionExtensions
{
    public static IServiceCollection AddAccounts(this IServiceCollection services, string connectionString)
    {
        services.AddAccountsInfrastructure(connectionString)
            .AddSignInManager();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

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
