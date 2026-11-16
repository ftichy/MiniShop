using Microsoft.AspNetCore.StaticFiles;

namespace MiniShop.Web.Ui;

/// <summary>
/// Vzhled krok D: soubory instalovatelné aplikace (PWA) ve <c>wwwroot</c> – manifest, service worker, offline stránka.
/// Jsou veřejné (statické soubory jdou před přihlášením). Service worker a manifest se posílají s
/// <c>Cache-Control: no-cache</c>: prohlížeč se tak u každé kontroly zeptá serveru a po nasazení dostane novou verzi
/// (jinak by telefon držel starý service worker).
/// </summary>
public static class PwaFiles
{
    public const string Manifest = "/manifest.webmanifest";
    public const string ServiceWorker = "/sw.js";
    public const string OfflinePage = "/offline.html";

    public static readonly IReadOnlyList<string> NoCachePaths = [Manifest, ServiceWorker, OfflinePage];

    public static void ApplyCacheHeaders(StaticFileResponseContext context)
    {
        if (NoCachePaths.Contains(context.Context.Request.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
}
