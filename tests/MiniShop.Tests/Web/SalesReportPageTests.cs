using System.Net;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 11: stránka Přehled tržeb pro admina. Mikroservisy M1: čtecí model drží služba Reporting.Api – stránka
/// se ptá přes HTTP (v testu <see cref="TestDoubles.FakeSalesReportApi"/>), přestavění pošle službě, a když služba
/// neběží, ukáže upozornění místo chyby 500.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class SalesReportPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Prehled_AdminVidiStrankuAPrestaveniPosleSluzbe()
    {
        web.SalesReports.Available = true;
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Sales"));
        Assert.Contains("Přehled tržeb", page);
        Assert.Contains("data-track=\"sales-rebuild\"", page);
        Assert.Contains("Potvrzené (i balené a odeslané)", page);   // stav přišel jako text, stránka ho přeloží

        var before = web.SalesReports.RebuildRequests;
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(admin, "/Admin/Sales");
        var response = await admin.PostAsync("/Admin/Sales?handler=Rebuild", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before + 1, web.SalesReports.RebuildRequests);
    }

    [Fact]
    public async Task SluzbaNebezi_StrankaUkazeUpozorneni()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        web.SalesReports.Available = false;
        try
        {
            var response = await admin.GetAsync("/Admin/Sales");
            var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("data-track=\"sales-unavailable\"", page);
            Assert.DoesNotContain("data-track=\"sales-rebuild\"", page);
        }
        finally
        {
            web.SalesReports.Available = true;
        }
    }
}
