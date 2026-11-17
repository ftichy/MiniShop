using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MiniShop.Web.Ui;

namespace MiniShop.Tests.Web;

/// <summary>
/// Vzhled krok D: MiniShop jako instalovatelná aplikace (PWA) – manifest, service worker, offline stránka, spodní lišta.
/// Chování service workeru v prohlížeči ověřuje E2E (MobilTests); tady pravidla, která se dají přečíst ze souborů.
/// </summary>
[Collection(WebCollection.Name)]
public sealed partial class PwaTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Manifest_JeVerejny_InstalovatelnyAIkonyExistuji()
    {
        var client = web.CreateBrowser();

        var response = await client.GetAsync(PwaFiles.Manifest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache, "manifest bez Cache-Control: no-cache");
        using var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = manifest.RootElement;
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.Equal("/Shop", root.GetProperty("start_url").GetString());

        // Chrome nabídne instalaci jen s ikonami 192 a 512 px; Android potřebuje i „maskable“ (ořízne si ji do kruhu).
        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "512x512");
        Assert.Contains(icons, icon => icon.GetProperty("purpose").GetString() == "maskable");
        foreach (var src in icons.Select(icon => icon.GetProperty("src").GetString()!))
        {
            var icon = await client.GetAsync(src);
            Assert.True(icon.StatusCode == HttpStatusCode.OK, $"ikona {src} chybí");
            Assert.Equal("image/png", icon.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task ServiceWorker_StrankyNeuklada_JenStatickeSouboryZVlastnihoWebu()
    {
        var response = await web.CreateBrowser().GetAsync(PwaFiles.ServiceWorker);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache, "sw.js bez Cache-Control: no-cache – telefon by držel starou verzi");
        var script = await response.Content.ReadAsStringAsync();

        // POST (formuláře, platba) a cizí weby (Stripe, Google) service worker neobsluhuje.
        Assert.Contains("request.method !== 'GET'", script);
        Assert.Contains("url.origin !== self.location.origin", script);
        // Stránky (HTML s objednávkami, adresami) jen ze sítě – do mezipaměti se ukládají jen statické cesty.
        Assert.Contains("request.mode === 'navigate'", script);
        // E-shop krok 23: oznámení push – ukázat a klepnutím otevřít jen stránku MiniShopu (cizí adresu ze zprávy ne).
        Assert.Contains("addEventListener('push'", script);
        Assert.Contains("addEventListener('notificationclick'", script);
        Assert.Contains("target.origin === self.location.origin", script);
        Assert.Equal(HttpStatusCode.OK, (await web.CreateBrowser().GetAsync("/icons/badge-96.png")).StatusCode);

        var staticPaths = ArrayItems(script, "STATIC_PATHS");
        string[] allowed = ["/css/", "/js/", "/lib/", "/icons/", "/media/products/"];
        Assert.All(staticPaths, path => Assert.Contains(path, allowed));
    }

    [Fact]
    public async Task ServiceWorker_OtiskPrecacheOdpovidaSouborum()
    {
        // Změna offline stránky nebo ikony musí změnit sw.js – jinak by telefon nainstalovanou starou verzi nevyměnil.
        var client = web.CreateBrowser();
        var script = await client.GetStringAsync(PwaFiles.ServiceWorker);

        using var content = new MemoryStream();
        foreach (var url in ArrayItems(script, "PRECACHE"))
        {
            var bytes = await client.GetByteArrayAsync(url);
            if (url.EndsWith(".html", StringComparison.Ordinal))
            {
                // Konce řádků se liší Windows/Linux (git) – otisk se počítá z textu bez \r.
                bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r", string.Empty));
            }

            content.Write(bytes);
        }

        var expected = Convert.ToHexStringLower(SHA256.HashData(content.ToArray()))[..12];
        Assert.True(script.Contains($"const PRECACHE_VERSION = '{expected}';"),
            $"V sw.js nastavte const PRECACHE_VERSION = '{expected}'; (obsah offline stránky nebo ikony se změnil).");
    }

    [Fact]
    public async Task OfflineStranka_JeVerejnaASamostatna()
    {
        var response = await web.CreateBrowser().GetAsync(PwaFiles.OfflinePage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Jste offline", html);
        Assert.DoesNotContain("<script", html);   // CSP i jednoduchost – bez sítě se nic dalšího nenačte
        Assert.DoesNotContain(".css", html);
    }

    [Fact]
    public async Task Layout_OdkazujeManifest_SpodniListaPodlePrihlaseni()
    {
        var anonymous = WebUtility.HtmlDecode(await web.CreateBrowser().GetStringAsync("/"));
        var customer = WebUtility.HtmlDecode(await (await web.LoginAsync(MiniShopWebFactory.UserEmail, MiniShopWebFactory.UserPassword))
            .GetStringAsync("/Shop"));

        Assert.Contains("<link rel=\"manifest\" href=\"/manifest.webmanifest\" />", anonymous);
        Assert.Contains("/js/pwa.js", anonymous);
        Assert.Contains("data-track=\"tab-login\"", anonymous);
        Assert.DoesNotContain("data-track=\"tab-cart\"", anonymous);   // košík jen zákazník (jako v hlavičce)

        Assert.Contains("data-track=\"tab-cart\"", customer);
        Assert.Contains("data-track=\"tab-orders\"", customer);
        Assert.Matches("class=\"tab-bar-item active\"[^>]*data-track=\"tab-shop\"", customer);   // jsem ve výloze
    }

    /// <summary>Položky pole v JavaScriptu: <c>const NAME = ['/a', '/b'];</c>.</summary>
    private static IReadOnlyList<string> ArrayItems(string script, string name)
    {
        var match = Regex.Match(script, $@"const {name} = \[(?<items>[^\]]*)\];");
        Assert.True(match.Success, $"V sw.js chybí const {name} = [...]");
        return QuotedRegex().Matches(match.Groups["items"].Value).Select(m => m.Groups[1].Value).ToList();
    }

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex QuotedRegex();
}
