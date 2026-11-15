using System.Net;
using Microsoft.AspNetCore.Http;
using MiniShop.Application.Abstractions;
using MiniShop.Web.Ui;

namespace MiniShop.Tests.Web;

/// <summary>
/// Stránkování seznamů: oprava nesmyslných parametrů, stránka za koncem, okno odkazů, adresy zachovají filtry –
/// a přes skutečné HTTP stránky Uživatelé (hledání podle e-mailu) a Objednávky (hledání podle čísla).
/// </summary>
[Collection(WebCollection.Name)]
public sealed class PagingTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-strankovani-1";

    [Fact]
    public void PageRequest_NesmyslneHodnoty_SeOpravi()
    {
        Assert.Equal(new PageRequest(1, PageRequest.DefaultSize), PageRequest.Of(null, null));
        Assert.Equal(new PageRequest(1, 50), PageRequest.Of(-3, 50));
        Assert.Equal(new PageRequest(4, PageRequest.DefaultSize), PageRequest.Of(4, 33));   // velikost jen z nabídky
        Assert.Equal(3, PageRequest.Of(99, 10).ClampTo(25).Page);                           // za koncem → poslední
        Assert.Equal(1, PageRequest.Of(5, 10).ClampTo(0).Page);                             // prázdný seznam = 1 stránka
    }

    [Fact]
    public void PagedList_VPameti_PocitaRozsah()
    {
        var page = Enumerable.Range(1, 45).ToList().ToPagedList(PageRequest.Of(3, 20));

        Assert.Equal([41, 42, 43, 44, 45], page.Items);
        Assert.Equal((3, 41, 45, 45), (page.PageCount, page.FirstItem, page.LastItem, page.TotalCount));
        Assert.Equal((0, 0), (new PagedList<int>([], 1, 20, 0).FirstItem, new PagedList<int>([], 1, 20, 0).LastItem));
    }

    [Fact]
    public void Pager_OknoStranek_AAdresaZachovaFiltry()
    {
        var pager = new PagerModel(Page: 6, PageCount: 20, Size: 20, TotalCount: 400, FirstItem: 101, LastItem: 120, Track: "t");
        Assert.Equal([1, null, 4, 5, 6, 7, 8, null, 20], pager.Window());
        Assert.Equal([1, 2, 3], (pager with { Page = 1, PageCount = 3 }).Window());

        var context = new DefaultHttpContext();
        context.Request.Path = "/Admin/Users";
        context.Request.QueryString = new QueryString("?q=jan&p=4&size=50&handler=Lock");
        Assert.Equal("/Admin/Users?q=jan&p=2&size=50", PagerModel.Url(context.Request, 2, 50));
        Assert.Equal("/Admin/Users?q=jan", PagerModel.Url(context.Request, 1, PageRequest.DefaultSize));   // výchozí hodnoty se nepíšou
    }

    [Fact]
    public async Task Uzivatele_HledaniAStranky()
    {
        var prefix = $"strankovani-{Guid.NewGuid():N}"[..24];
        for (var i = 1; i <= 12; i++)
        {
            await web.CreateUserAsync($"{prefix}-{i:00}@test.local", Password);
        }

        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var first = await PageAsync(admin, $"/Admin/Users?q={prefix}&size=10");
        var second = await PageAsync(admin, $"/Admin/Users?q={prefix}&size=10&p=2");
        var beyond = await PageAsync(admin, $"/Admin/Users?q={prefix}&size=10&p=99");

        Assert.Contains("Zobrazeno 1–10 z 12", first);
        Assert.Contains($"{prefix}-10@test.local", first);
        Assert.DoesNotContain($"{prefix}-11@test.local", first);
        Assert.Contains("Zobrazeno 11–12 z 12", second);
        Assert.Contains($"{prefix}-12@test.local", second);
        Assert.Contains("Zobrazeno 11–12 z 12", beyond);   // stránka za koncem = poslední
        Assert.Contains($"href=\"/Admin/Users?q={prefix}&p=2&size=10\"", WebUtility.HtmlDecode(first));   // odkaz zachová hledání
    }

    [Fact]
    public async Task Objednavky_StrankyAHledaniPodleCisla_JenVlastni()
    {
        var email = $"strankovani-obj-{Guid.NewGuid():N}@test.local";
        await web.CreateUserAsync(email, Password);
        var orderIds = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            orderIds.Add(await web.CreateOrderForAsync(email));
        }

        var customer = await web.LoginAsync(email, Password);

        var page = await PageAsync(customer, "/Orders?size=10");
        var found = await PageAsync(customer, $"/Orders?order={orderIds[0]}");
        var foreign = await PageAsync(customer, $"/Orders?order={await ForeignOrderAsync()}");

        Assert.Contains("Zobrazeno 1–3 z 3", page);
        Assert.Contains($"Objednávka č. {orderIds[0]}", found);
        Assert.DoesNotContain($"Objednávka č. {orderIds[1]}<", found);
        Assert.Contains("nebyla nalezena", foreign);   // cizí objednávka se hledáním nenajde
    }

    private async Task<int> ForeignOrderAsync()
    {
        var other = $"strankovani-cizi-{Guid.NewGuid():N}@test.local";
        await web.CreateUserAsync(other, Password);
        return await web.CreateOrderForAsync(other);
    }

    private static async Task<string> PageAsync(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url));
}
