using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiniShop.ServiceDefaults;

/// <summary>Nastavení ověření servisním klíčem – očekávaný klíč z konfigurace.</summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Účty krok 7: služba (sklad, od mikroservis M1 i přehled tržeb) pustí na /api jen volajícího se správným servisním
/// klíčem v hlavičce X-Api-Key (= Web). Kdo z uživatelů smí co, rozhoduje Web (role); služba ověřuje jen, že volá Web.
/// </summary>
/// <remarks>
/// Bez hlavičky = „nepřihlášený“ (NoResult), se špatným klíčem = chyba; obojí skončí 401 (fallback policy).
/// Porovnání v konstantním čase – podle doby odpovědi nejde klíč hádat znak po znaku.
/// </remarks>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ServiceApiKey.HeaderName, out var provided) || provided.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided.ToString()),
            Encoding.UTF8.GetBytes(Options.ApiKey));
        if (!matches)
        {
            // Hodnotu klíče nikdy do logu.
            Logger.LogWarning("Požadavek se špatným servisním klíčem");
            return Task.FromResult(AuthenticateResult.Fail("Neplatný servisní klíč."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "minishop-web")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class ApiKeyAuthenticationExtensions
{
    /// <summary>
    /// Zero trust služby: co není výslovně [AllowAnonymous] (health checky), to vyžaduje servisní klíč – i čtení.
    /// </summary>
    public static IServiceCollection AddServiceApiKeyAuthentication(this IServiceCollection services, string apiKey)
    {
        services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, options => options.ApiKey = apiKey);
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }
}
