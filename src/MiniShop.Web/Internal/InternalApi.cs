using Microsoft.AspNetCore.Authorization;
using MiniShop.ServiceDefaults;

namespace MiniShop.Web.Internal;

/// <summary>
/// Mikroservisy M2A: interní API Webu (<c>/internal/…</c>) pro služby v síti Compose – od M2B se jím Notifications ptá
/// na data modulů, které běží ve Webu (účty, kopie katalogu, faktury). Lidé se přihlašují cookie, služby servisním klíčem;
/// tahle politika pustí JEN klíč – cookie (i admina) tu neplatí a klíč zase neotevře stránky (výchozí schéma je cookie).
/// </summary>
/// <remarks>Z internetu je <c>/internal/*</c> zavřené už v Caddy (404) – klíč je druhá vrstva.</remarks>
public static class InternalApi
{
    public const string PolicyName = "RequireServiceApiKey";

    public static IServiceCollection AddWebInternalApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Bez klíče se Web nespustí (fail fast) – stejně jako u klíče skladu a přehledu tržeb.
        var apiKey = ServiceApiKey.Read(key => configuration[key], ServiceApiKey.WebInternalConfigurationKey);

        // Druhé schéma ověření vedle cookie – výchozí zůstává cookie, klíč se čte jen tam, kde o něj politika požádá.
        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, options => options.ApiKey = apiKey);

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());

        return services;
    }
}
