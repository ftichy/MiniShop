using System.Net;
using System.Text.RegularExpressions;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Web;

/// <summary>
/// Vzhled krok F2: živé výřezy (tag helper <c>live-region</c>) – dokud se na něco čeká, výřez nese atributy htmx
/// (každé 3 s stáhne tutéž stránku a vymění jen sebe); když je hotovo, atributy chybí a dotazování samo skončí.
/// Celé obnovení stránky (<c>orders-reload.js</c>) už nikde není.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class LiveRegionTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task DetailObjednavky_CekaNaSklad_ZivyVyrez_PotvrzenaUzNe()
    {
        var (customer, pendingId) = await CustomerAsync("zivy-1@example.com");
        var confirmedId = await web.CreateConfirmedOrderForAsync("zivy-1@example.com", ShippingMethod.Pickup);

        var pending = await customer.GetStringAsync($"/Orders/{pendingId}");
        var confirmed = await customer.GetStringAsync($"/Orders/{confirmedId}");

        AssertLive(pending, "order-live", $"/Orders/{pendingId}");
        AssertNotLive(confirmed, "order-live");
        Assert.DoesNotContain("orders-reload.js", pending);
        Assert.DoesNotContain("data-reload-when-idle", pending);
    }

    [Fact]
    public async Task MojeObjednavky_NekteraCeka_ZivySeznamSHledanimMimoVyrez()
    {
        var (customer, _) = await CustomerAsync("zivy-2@example.com");

        var page = await customer.GetStringAsync("/Orders?p=1");

        AssertLive(page, "orders-live", "/Orders?p=1");   // stejné parametry (stránkování) i v dotazu výřezu
        var region = page.IndexOf("id=\"orders-live\"", StringComparison.Ordinal);
        Assert.True(page.IndexOf("name=\"order\"", StringComparison.Ordinal) < region, "Hledání má být nad výřezem – jinak by ho výměna přepsala.");
    }

    [Fact]
    public async Task Platba_SeZaklada_ZivyVyrez_PripravenaSTlacitkyUzNe()
    {
        var (customer, orderId) = await CustomerAsync("zivy-3@example.com");

        var preparing = await customer.GetStringAsync($"/Pay/{orderId}");   // sága platbu ještě nezaložila

        AssertLive(preparing, "pay-live", $"/Pay/{orderId}");
        Assert.Contains("data-track=\"pay-preparing\"", preparing);
    }

    [Fact]
    public async Task Expedice_BezPredaniDopravci_NeniZiva()
    {
        await web.CreateUserAsync("zivy-4@example.com", MiniShopWebFactory.UserPassword);
        await web.CreateConfirmedOrderForAsync("zivy-4@example.com", ShippingMethod.Courier);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var page = await admin.GetStringAsync("/Admin/Fulfilment");

        Assert.Contains("id=\"fulfilment-live\"", page);
        Assert.DoesNotContain("orders-reload.js", page);
    }

    [Fact]
    public async Task ZivyVyrez_SkriptPojistkyNaKazdeStrance()
    {
        var html = await web.CreateBrowser().GetStringAsync("/Shop");

        Assert.Matches("src=\"/js/live-region\\.js\\?v=[^\"]+\"", html);
        Assert.DoesNotContain("orders-reload.js", html);
    }

    /// <summary>Výřez s id, který se sám obnovuje: htmx stáhne tutéž adresu a vybere z ní sám sebe.</summary>
    internal static void AssertLive(string html, string id, string url)
    {
        var tag = RegionTag(html, id);
        Assert.Contains("data-live-region", tag);
        Assert.Contains($"hx-get=\"{url}\"", WebUtility.HtmlDecode(tag));
        Assert.Contains("hx-trigger=\"every 3s\"", tag);
        Assert.Contains($"hx-select=\"#{id}\"", tag);
        Assert.Contains("hx-swap=\"outerHTML\"", tag);
    }

    internal static void AssertNotLive(string html, string id)
    {
        var tag = RegionTag(html, id);
        Assert.Contains("data-live-region", tag);   // značka zůstává, jen se už nedotazuje
        Assert.DoesNotContain("hx-trigger", tag);
        Assert.DoesNotContain("hx-get", tag);
    }

    private static string RegionTag(string html, string id) =>
        Regex.Match(html, $"<div[^>]*id=\"{id}\"[^>]*>").Value is { Length: > 0 } tag
            ? tag
            : throw new Xunit.Sdk.XunitException($"Výřez #{id} na stránce není.");

    private async Task<(HttpClient Customer, int PendingOrderId)> CustomerAsync(string email)
    {
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        var orderId = await web.CreateOrderForAsync(email);
        return (await web.LoginAsync(email, MiniShopWebFactory.UserPassword), orderId);
    }
}
