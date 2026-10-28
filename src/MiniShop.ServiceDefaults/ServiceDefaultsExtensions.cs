using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

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
    /// - <b>OpenTelemetry</b> (krok 11): trasy, metriky a logy do Aspire dashboardu, viz <see cref="ConfigureOpenTelemetry"/>.
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

        builder.ConfigureOpenTelemetry();

        return builder;
    }

    /// <summary>
    /// Krok 11: OpenTelemetry – tři pilíře observability.
    /// - <b>Trasy</b>: HTTP požadavky, volání HttpClientu, SQL příkazy a naše zprávy (zdroj "MiniShop.Messaging").
    /// - <b>Metriky</b>: ASP.NET Core, HttpClient, runtime (GC, vlákna) a naše čítače outboxu a consumerů.
    /// - <b>Logy</b>: přes ILogger (Serilog je předá dál, writeToProviders) – v dashboardu propojené s trasou.
    /// Ven jde všechno protokolem OTLP na adresu z OTEL_EXPORTER_OTLP_ENDPOINT – tu nastaví Aspire (= jeho dashboard).
    /// Bez Aspire proměnná chybí a nic se neexportuje; trace ID ale vznikají dál (jsou v logu i v outboxu).
    /// </summary>
    private static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(MiniShopSources))
            .WithTracing(tracing => tracing
                .AddSource(MiniShopSources)
                // Health checky volá Aspire každých pár sekund – do tras nepatří.
                .AddAspNetCoreInstrumentation(options => options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/alive"))
                .AddHttpClientInstrumentation()
                // SQL jen jako součást nějaké operace. Workery se každé 2 s ptají outboxu "je tu něco?" –
                // bez rodiče (Activity.Current == null) by každý takový dotaz byl samostatná trasa a zahltil dashboard.
                .AddEntityFrameworkCoreInstrumentation(options => options.Filter = (_, _) => Activity.Current is not null));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>Vlastní zdroje tras a metrik MiniShopu (MessagingTelemetry.SourceName = "MiniShop.Messaging").</summary>
    private const string MiniShopSources = "MiniShop.*";

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Účty krok 2: zdraví musí být vidět bez přihlášení (UptimeRobot, Docker healthcheck),
        // i když Web jinak všechno zamyká (fallback policy).
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapHealthChecks("/alive", new() { Predicate = check => check.Tags.Contains("live") }).AllowAnonymous();

        return app;
    }
}
