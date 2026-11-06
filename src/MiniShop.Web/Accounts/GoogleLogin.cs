using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Účty krok 10: přihlášení přes Google (OpenID Connect / OAuth 2.0). MiniShop přesměruje na Google, ten ověří
/// uživatele a vrátí ho na <c>/signin-google</c> s jednorázovým kódem; ASP.NET Core ho u Googlu vymění za identitu
/// (stálé ID <c>sub</c>, e-mail, „e-mail ověřen“, jméno) a uloží ji do dočasné cookie (<c>IdentityConstants.ExternalScheme</c>).
/// Co s ní dál, rozhoduje <see cref="ExternalAccountLinker"/>. Heslo ke Googlu MiniShop nikdy nevidí.
/// </summary>
public static class GoogleLogin
{
    /// <summary>Jméno schématu – i hodnota LoginProvider v tabulce AspNetUserLogins.</summary>
    public const string Scheme = GoogleDefaults.AuthenticationScheme;

    public const string DisplayName = "Google";

    /// <summary>Claim „Google ověřil e-mail“ (z <c>email_verified</c> – výchozí mapování ho nepřenáší).</summary>
    public const string EmailVerifiedClaim = "minishop:google_email_verified";

    /// <summary>
    /// Zapne Google, jen když je nastavený OAuth klient (<c>Authentication:Google:ClientId</c> + <c>ClientSecret</c>).
    /// Bez nastavení tlačítko „Přihlásit přes Google“ zmizí – jako e-maily bez <c>Email:Host</c>.
    /// </summary>
    public static AuthenticationBuilder AddGoogleLogin(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        var clientId = configuration["Authentication:Google:ClientId"];
        var clientSecret = configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return builder;
        }

        return builder.AddGoogle(Scheme, DisplayName, options =>
        {
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            // Identita z Googlu jde do dočasné cookie Identity (5 minut), účet MiniShopu vybere až stránka ExternalLogin.
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");
            // Přístupový token Googlu nepotřebujeme (nevoláme jeho API) – do cookie ho neukládat.
            options.SaveTokens = false;
            options.Events.OnRemoteFailure = RedirectAfterFailureAsync;
        });
    }

    /// <summary>
    /// Zrušení na Googlu (Zrušit / access_denied), vypršelý nebo podvržený návrat: bez tohoto by handler skončil
    /// výjimkou (500). Vrátí uživatele tam, kam měl Google vrátit, s <c>remoteError</c> – stránka ukáže hlášku.
    /// </summary>
    private static Task RedirectAfterFailureAsync(RemoteFailureContext context)
    {
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(GoogleLogin))
            .LogWarning("Přihlášení přes Google selhalo: {Reason}", context.Failure?.Message);

        // RedirectUri je vždy naše místní adresa (nastavuje ji MiniShop); bez ní (neplatný state) přihlášení.
        var target = context.Properties?.RedirectUri is { } redirectUri && redirectUri.StartsWith('/')
            ? redirectUri
            : "/Account/ExternalLogin?handler=Callback";
        context.Response.Redirect(QueryHelpers.AddQueryString(target, "remoteError", "google"));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>Identita z dočasné cookie → vstup pro <see cref="ExternalAccountLinker"/>.</summary>
    public static ExternalAccount ToExternalAccount(this ExternalLoginInfo info) => new(
        info.LoginProvider,
        info.ProviderKey,
        info.ProviderDisplayName ?? info.LoginProvider,
        info.Principal.FindFirstValue(ClaimTypes.Email),
        string.Equals(info.Principal.FindFirstValue(EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase),
        info.Principal.FindFirstValue(ClaimTypes.Name));

    /// <summary>
    /// Smazání účtu bez hesla: Google se má znovu zeptat na přihlášení (<c>prompt=login</c>), ne pustit uživatele
    /// rovnou ze své otevřené relace – jinak by účet smazal kdokoli u odemčeného prohlížeče.
    /// </summary>
    public static void RequireFreshLogin(AuthenticationProperties properties) =>
        properties.SetParameter(GoogleChallengeProperties.PromptParameterKey, "login");
}
