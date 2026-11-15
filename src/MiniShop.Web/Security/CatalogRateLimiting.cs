using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MiniShop.Web.Security;

/// <summary>Nastavení limitu našeptávače (sekce RateLimiting:CatalogSuggest).</summary>
public sealed class CatalogSuggestRateLimitOptions
{
    public const string SectionName = "RateLimiting:CatalogSuggest";

    /// <summary>
    /// Kolik dotazů našeptávače smí jedna IP adresa za okno. Skript čeká 200 ms po dopsání, takže i rychlé psaní
    /// dá jednotky dotazů za sekundu; 120 za minutu stačí i několika lidem za jednou IP (domácí síť).
    /// </summary>
    public int PermitLimit { get; set; } = 120;

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>E-shop krok 21: limit pro veřejný našeptávač <c>/api/catalog/suggest</c> – po překročení 429.</summary>
/// <remarks>Limity jsou v paměti každé kopie Webu (škálování krok 5) – za Caddy se tak reálný limit násobí počtem kopií.</remarks>
public static class CatalogRateLimiting
{
    public const string PolicyName = "catalog-suggest";

    public static IServiceCollection AddCatalogRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CatalogSuggestRateLimitOptions.SectionName).Get<CatalogSuggestRateLimitOptions>()
            ?? new CatalogSuggestRateLimitOptions();

        services.AddRateLimiter(limiter => limiter.AddPolicy(PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0
                })));

        return services;
    }
}
