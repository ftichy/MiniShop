using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Účty krok 2: kdo má příznak „musí změnit heslo“ (první admin se známým heslem), nedostane se nikam jinam
/// než na změnu hesla nebo odhlášení. Stránky přesměruje, API odpoví 403.
/// </summary>
public static class MustChangePasswordMiddleware
{
    private const string ChangePasswordPath = "/Account/ChangePassword";

    /// <summary>Patří za UseAuthentication – až tam je známý přihlášený uživatel.</summary>
    public static IApplicationBuilder UseMustChangePassword(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var mustChange = context.User.HasClaim(claim => claim.Type == AccountClaimTypes.MustChangePassword);

            if (!mustChange
                || path.StartsWithSegments(ChangePasswordPath)
                || path.StartsWithSegments("/Account/Logout"))
            {
                await next(context);
                return;
            }

            if (path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.Response.Redirect(ChangePasswordPath);
        });
}
