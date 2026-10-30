using System.Diagnostics;
using System.Security.Claims;

namespace MiniShop.Web.Security;

/// <summary>
/// Účty krok 8: bezpečnostní hlavičky – „provozní řád“ pro prohlížeč. Nastavuje je aplikace (ne Caddy),
/// takže platí i lokálně a hlídají je testy.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Content Security Policy: odkud smí stránka načíst skripty, styly, obrázky… Jen z MiniShopu ('self').
    /// Kdyby se do stránky přece jen dostal cizí skript (XSS), prohlížeč ho nespustí – inline skripty proto
    /// v MiniShopu nejsou, jen soubory ve wwwroot/js. Styly 'unsafe-inline': Mermaid kreslí diagramy s inline styly
    /// a pár atributů style="" v šablonách (styl nespouští kód).
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Vývoj: hot reload / obnovení prohlížeče z Visual Studia mluví přes WebSocket na jiném portu.</summary>
    private const string DevelopmentConnectSources = " ws://localhost:* wss://localhost:*";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IHostEnvironment environment)
    {
        var csp = environment.IsDevelopment()
            ? ContentSecurityPolicy.Replace("connect-src 'self'", "connect-src 'self'" + DevelopmentConnectSources)
            : ContentSecurityPolicy;

        return app.Use((context, next) =>
        {
            // OnStarting: hlavičky dostane i odpověď, kterou později přepíše exception handler (chybová stránka).
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = csp;
                headers.XContentTypeOptions = "nosniff";                  // neodhadovat typ souboru (text → skript)
                headers.XFrameOptions = "DENY";                           // nevkládat do cizí stránky (clickjacking), starší prohlížeče
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";   // odkaz s tokenem neodejde jinému webu
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                return Task.CompletedTask;
            });
            return next(context);
        });
    }

    /// <summary>
    /// Účty krok 8: do trasy (OpenTelemetry) jen ID přihlášeného uživatele – tag „enduser.id“ podle sémantických
    /// konvencí. E-mail ani jméno ne (osobní údaje by skončily v dashboardu).
    /// </summary>
    public static IApplicationBuilder UseEndUserTracing(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
            {
                Activity.Current?.SetTag("enduser.id", userId);
            }

            return next(context);
        });
}
