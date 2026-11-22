using System.Net;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>Proud B2: stránka Rychlost vyřízení – data a záložka ze služby (podvržené), přestavění z proudu, výpadek služby.</summary>
[Collection(WebCollection.Name)]
public sealed class LeadTimesPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Sklad_VidiDobyAZalozku_PrestaveniPozadaSluzbu()
    {
        var client = await WarehouseAsync("rychlost-sklad@example.com");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/LeadTimes"));
        var before = web.SalesReports.LeadTimeRebuildRequests;
        var response = await client.PostAsync("/Admin/LeadTimes?handler=Rebuild", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Admin/LeadTimes")
        }));

        Assert.Contains("data-track=\"lead-times-table\"", html);
        Assert.Contains("6,5 s", html);                 // p50 potvrzení z podvržené služby
        Assert.Contains("zpráva č. 41", html);          // záložka v proudu
        Assert.Contains("přestavěno 2×", html);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before + 1, web.SalesReports.LeadTimeRebuildRequests);
    }

    [Fact]
    public async Task ZivyVyrez_SignalZmenyObjednavek_PoPrestaveniDotazovani()
    {
        var client = await WarehouseAsync("rychlost-zive@example.com");

        var normal = await client.GetStringAsync("/Admin/LeadTimes");
        var response = await client.PostAsync("/Admin/LeadTimes?handler=Rebuild", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Admin/LeadTimes")
        }));
        var location = response.Headers.Location!.OriginalString;
        var rebuilding = await client.GetStringAsync(location);
        var longAgo = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var afterWindow = await client.GetStringAsync($"/Admin/LeadTimes?rebuilt={longAgo}");

        // Běžně: signál při každé změně stavu objednávky, obnovení 2 s po něm (projekci počítá Reporting.Api).
        Assert.Contains("id=\"lead-times-live\"", normal);
        Assert.Contains("data-live-topic=\"staff:orders\"", normal);
        Assert.Contains("data-live-delay=\"2000\"", normal);

        // Po přestavění: adresa nese čas kliknutí, výřez bez tématu = dotazování každé 3 s (i obnovení výřezu má tutéž adresu).
        Assert.Contains("rebuilt=", location);
        Assert.Contains("hx-get=\"/Admin/LeadTimes?rebuilt=", rebuilding);
        Assert.DoesNotContain("data-live-topic", rebuilding);

        // Po 30 s zase signál.
        Assert.Contains("data-live-topic=\"staff:orders\"", afterWindow);
    }

    [Fact]
    public async Task VypadekSluzby_Upozorneni()
    {
        web.SalesReports.Available = false;
        try
        {
            var client = await WarehouseAsync("rychlost-vypadek@example.com");

            var response = await client.GetAsync("/Admin/LeadTimes");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("data-track=\"lead-times-unavailable\"", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            web.SalesReports.Available = true;
        }
    }

    [Fact]
    public void Format_DobyProCloveka()
    {
        Assert.Equal("—", MiniShop.Web.Pages.Admin.LeadTimes.IndexModel.Format(null));
        Assert.Equal("3 min 20 s", MiniShop.Web.Pages.Admin.LeadTimes.IndexModel.Format(200));
        Assert.Equal("2 h 5 min", MiniShop.Web.Pages.Admin.LeadTimes.IndexModel.Format(7500));
    }

    private async Task<HttpClient> WarehouseAsync(string email)
    {
        await web.CreateWithRolesAsync(email, MiniShopWebFactory.UserPassword, AccountRoles.Warehouse);
        return await web.LoginAsync(email, MiniShopWebFactory.UserPassword);
    }
}
