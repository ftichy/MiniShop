using System.Net;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Web;

/// <summary>
/// Mikroservisy M2B: stránka Zprávy ukáže provoz zpráv VŠECH služeb – Web v procesu, sklad a přehled tržeb přes jejich
/// /api/messages (v testu <see cref="FakeServiceMessages"/>). „Zkusit znovu“ a „Vrátit do fronty“ jdou do správné služby
/// a nedostupná služba nerozbije stránku – ukáže se jen u ní upozornění.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class MessagesPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task VsechnySluzby_NaJedneStrance()
    {
        var admin = await LoginAsync();

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Messages"));

        Assert.Contains("data-track=\"messages-service-web\"", page);
        Assert.Contains("data-track=\"messages-service-inventory-api\"", page);
        Assert.Contains("data-track=\"messages-service-reporting-api\"", page);
        Assert.Contains("minishop.reporting.dead", page);
        Assert.Contains("Ordering", page);   // moduly Webu v procesu
        Assert.DoesNotContain("messages-service-unavailable", page);
    }

    [Fact]
    public async Task ZkusitZnovuAVratitDoFronty_JdouDoSpravneSluzby()
    {
        var admin = await LoginAsync();
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(admin, "/Admin/Messages");
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

        var retry = await admin.PostAsync(
            $"/Admin/Messages?handler=Retry&service=inventory-api&module=Inventory&id={FakeServiceMessages.PoisonId}", form);
        var requeue = await admin.PostAsync("/Admin/Messages?handler=Requeue&service=reporting-api&queue=minishop.reporting", form);

        Assert.Equal((HttpStatusCode.Redirect, HttpStatusCode.Redirect), (retry.StatusCode, requeue.StatusCode));
        Assert.Contains(("Inventory", FakeServiceMessages.PoisonId), web.InventoryMessages.Retried);
        Assert.Contains("minishop.reporting", web.ReportingMessages.Requeued);
        Assert.DoesNotContain("minishop.reporting", web.InventoryMessages.Requeued);
    }

    [Fact]
    public async Task NedostupnaSluzba_UpozorneniJenUNi()
    {
        var admin = await LoginAsync();
        web.ReportingMessages.Available = false;
        try
        {
            var response = await admin.GetAsync("/Admin/Messages");
            var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("data-track=\"messages-service-unavailable\"", page);
            Assert.Contains("minishop.inventory.dead", page);   // ostatní služby normálně
            Assert.DoesNotContain("minishop.reporting.dead", page);
        }
        finally
        {
            web.ReportingMessages.Available = true;
        }
    }

    private Task<HttpClient> LoginAsync() => web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
}
