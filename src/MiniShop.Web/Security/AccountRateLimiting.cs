using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MiniShop.Web.Security;

/// <summary>Nastavení limitu (sekce RateLimiting:AccountForms) – testy ho zvednou, aby si nepřekážely.</summary>
public sealed class AccountFormsRateLimitOptions
{
    public const string SectionName = "RateLimiting:AccountForms";

    /// <summary>Kolik odeslání formulářů účtu smí jedna IP adresa za okno.</summary>
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Účty krok 8: rate limiting formulářů účtu (přihlášení, registrace, zapomenuté a nové heslo, poslat znovu,
/// změna hesla) – z jedné IP adresy nejvýš 10 odeslání za minutu, pak 429. Brzdí hádání hesel, zkoušení
/// uniklých hesel z jiných webů (credential stuffing) i zahlcení Gmailu e-maily.
/// </summary>
/// <remarks>
/// Počítají se jen POST (odeslání formuláře) – samotné zobrazení stránky ne. IP adresa klienta: za Caddy ji
/// doplní ForwardedHeaders z X-Forwarded-For (ASPNETCORE_FORWARDEDHEADERS_ENABLED v compose.yaml).
/// Limity jsou v paměti procesu – po restartu se počítá znovu (pro jednu instanci Webu stačí).
/// </remarks>
public static class AccountRateLimiting
{
    public const string PolicyName = "account-forms";

    public static IServiceCollection AddAccountRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AccountFormsRateLimitOptions.SectionName).Get<AccountFormsRateLimitOptions>()
            ?? new AccountFormsRateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = OnRejectedAsync;

            limiter.AddPolicy(PolicyName, context =>
                HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = options.PermitLimit,
                            Window = TimeSpan.FromSeconds(options.WindowSeconds),
                            QueueLimit = 0   // nečekat ve frontě – rovnou odmítnout
                        })
                    : RateLimitPartition.GetNoLimiter("get"));
        });

        return services;
    }

    /// <summary>Krátká česká stránka místo prázdné odpovědi 429 – uživatel ví, co se děje a kdy to zkusit znovu.</summary>
    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AccountRateLimiting))
            .LogWarning("Rate limiting: příliš mnoho odeslání formuláře {Path}", context.HttpContext.Request.Path);

        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(
            """
            <!DOCTYPE html>
            <html lang="cs"><head><meta charset="utf-8"><title>Příliš mnoho pokusů – MiniShop</title></head>
            <body><h1>Příliš mnoho pokusů</h1><p>Zkuste to prosím znovu za minutu.</p><p><a href="/">Zpět na MiniShop</a></p></body>
            </html>
            """, cancellationToken);
    }
}
