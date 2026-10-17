using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace MiniShop.ServiceDefaults;

/// <summary>
/// Krok 10B: věci, které chce mít každá služba stejně. Volá se jako první v Program.cs obou služeb.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// - <b>Service discovery</b>: HttpClient může volat "https+http://inventory-api" a adresu služby si najde
    ///   v konfiguraci (sekce "Services", pod Aspire ji tam doplní AppHost).
    /// - <b>Odolnost</b>: každý HttpClient dostane retry, timeout a circuit breaker (Microsoft.Extensions.Http.Resilience).
    /// - <b>Health checky</b>: /health (připravená přijímat požadavky) a /alive (proces žije) – Aspire podle nich ukazuje stav.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(options =>
            {
                // POST/PUT/DELETE neopakovat: kdyby odpověď jen nedorazila, opakování by např. založilo produkt dvakrát.
                options.Retry.DisableForUnsafeHttpMethods();

                // Volá to UI – při výpadku služby radši rychle ukázat "nedostupné" než nechat uživatele čekat.
                // Jeden opakovaný pokus, každý nejvýš 2 s.
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromMilliseconds(200);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);

                // Circuit breaker ("jistič"): když za 30 s selže aspoň polovina z min. 4 požadavků, na 15 s se
                // služba přestane volat a požadavek hned skončí chybou – stránka se načte okamžitě místo čekání
                // na timeouty. Pak jistič pustí zkušební požadavek a podle výsledku se zase zapne nebo zůstane vypnutý.
                options.CircuitBreaker.MinimumThroughput = 4;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });
            http.AddServiceDiscovery();
        });

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new() { Predicate = check => check.Tags.Contains("live") });

        return app;
    }
}
