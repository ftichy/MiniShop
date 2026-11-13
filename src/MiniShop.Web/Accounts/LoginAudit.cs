using Microsoft.AspNetCore.Identity;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Audit přihlášení (2FA krok B): převod výsledku <see cref="SignInResult"/> na <see cref="LoginResult"/> a zápis s IP klienta.
/// </summary>
/// <remarks>
/// IP bere z <c>Connection.RemoteIpAddress</c>. Za Caddy ji díky <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> (compose.yaml)
/// doplní middleware ForwardedHeaders z <c>X-Forwarded-For</c> – bez něj by tu byla IP proxy.
/// </remarks>
public static class LoginAudit
{
    public static string? ClientIp(this HttpContext httpContext) => httpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Zablokovaný adminem = zámek do roku 9999 (stejné pravidlo jako <c>AccountAdministration</c>).</summary>
    public static bool IsBlocked(ApplicationUser user) => user.LockoutEnd?.Year == DateTimeOffset.MaxValue.Year;

    /// <param name="wrongInput">Co se stalo, když přihlášení nevyšlo a nejde o zámek: špatné heslo, nebo špatný kód.</param>
    public static LoginResult Map(SignInResult result, ApplicationUser? user, LoginResult wrongInput)
    {
        if (result.Succeeded)
        {
            return LoginResult.Succeeded;
        }

        if (result.RequiresTwoFactor)
        {
            return LoginResult.AwaitingSecondFactor;
        }

        if (result.IsLockedOut)
        {
            return user is not null && IsBlocked(user) ? LoginResult.Blocked : LoginResult.LockedOut;
        }

        return result.IsNotAllowed ? LoginResult.UnconfirmedEmail : wrongInput;
    }

    public static Task RecordAsync(this LoginAttemptRecorder recorder, HttpContext httpContext, ApplicationUser? user,
        LoginMethod method, LoginResult result) =>
        recorder.RecordAsync(user?.Id, method, result, httpContext.ClientIp(), httpContext.RequestAborted);

    /// <summary>Text pro výpis (detail uživatele, stránka účtu).</summary>
    public static string MethodText(LoginMethod method) => method switch
    {
        LoginMethod.Password => "heslo",
        LoginMethod.AuthenticatorCode => "kód z aplikace",
        LoginMethod.RecoveryCode => "záchranný kód",
        LoginMethod.Google => "Google",
        _ => method.ToString()
    };

    public static string ResultText(LoginResult result) => result switch
    {
        LoginResult.Succeeded => "přihlášen",
        LoginResult.WrongPassword => "špatné heslo",
        LoginResult.WrongCode => "špatný kód",
        LoginResult.LockedOut => "účet zamčený",
        LoginResult.Blocked => "účet zablokovaný",
        LoginResult.UnconfirmedEmail => "nepotvrzený e-mail",
        LoginResult.Failed => "nepovedlo se",
        LoginResult.AwaitingSecondFactor => "první krok prošel, čeká se na kód",
        _ => result.ToString()
    };

    public static string Badge(LoginResult result) => result switch
    {
        LoginResult.Succeeded => "text-bg-success",
        LoginResult.AwaitingSecondFactor => "text-bg-secondary",
        _ => "text-bg-danger"
    };
}
