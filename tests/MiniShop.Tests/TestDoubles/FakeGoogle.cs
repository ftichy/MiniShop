using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniShop.Web.Accounts;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Účty krok 10: co si „Google“ v testech zapamatoval z posledního přesměrování (např. <c>prompt=login</c>).
/// </summary>
public sealed class FakeGoogle
{
    /// <summary>Kdo se „přihlásí u Googlu“ – hlavičky požadavku, který přesměrování spustil (test je nastaví).</summary>
    public const string IdHeader = "X-Fake-Google-Id";
    public const string EmailHeader = "X-Fake-Google-Email";
    public const string EmailVerifiedHeader = "X-Fake-Google-Email-Verified";
    public const string NameHeader = "X-Fake-Google-Name";

    public string? LastPrompt { get; set; }
}

/// <summary>
/// Účty krok 10: místo skutečného Googlu (přesměrování na accounts.google.com a zpět na /signin-google). Challenge
/// rovnou udělá to, co by udělal handler Googlu po návratu: uloží identitu do dočasné cookie Identity a přesměruje
/// na RedirectUri. Identitu bere z hlaviček požadavku; bez <see cref="FakeGoogle.IdHeader"/> = uživatel na Googlu zrušil.
/// </summary>
public sealed class FakeGoogleHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    FakeGoogle google)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        google.LastPrompt = properties.GetParameter<string>("prompt");
        var redirectUri = properties.RedirectUri ?? "/";
        var headers = Request.Headers;
        var id = headers[FakeGoogle.IdHeader].ToString();
        if (string.IsNullOrEmpty(id))
        {
            // Jako GoogleLogin.RedirectAfterFailureAsync po „Zrušit“ na Googlu.
            Response.Redirect(QueryHelpers.AddQueryString(redirectUri, "remoteError", "google"));
            return;
        }

        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
        AddIfPresent(identity, ClaimTypes.Email, headers[FakeGoogle.EmailHeader]);
        AddIfPresent(identity, ClaimTypes.Name, headers[FakeGoogle.NameHeader]);
        AddIfPresent(identity, GoogleLogin.EmailVerifiedClaim, headers[FakeGoogle.EmailVerifiedHeader]);

        // Stejně jako RemoteAuthenticationHandler: RedirectUri do cookie neukládat, přesměrovat až po přihlášení.
        properties.RedirectUri = null;
        await Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);
        Response.Redirect(redirectUri);
    }

    private static void AddIfPresent(ClaimsIdentity identity, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            identity.AddClaim(new Claim(type, value));
        }
    }
}
