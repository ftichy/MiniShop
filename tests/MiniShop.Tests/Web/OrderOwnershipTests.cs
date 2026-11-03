using System.Net;
using System.Net.Http.Json;
using MiniShop.Application.Ordering;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 3: vlastník objednávky přes skutečné HTTP. Zákazník vidí a ruší jen své objednávky; cizí se tváří
/// jako neexistující (404) – i když změní číslo v adrese (IDOR). Admin vidí všechny.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class OrderOwnershipTests(MiniShopWebFactory web) : IAsyncLifetime
{
    private const string Password = "heslo-pro-test-123";
    private readonly string _janEmail = $"jan-{Guid.NewGuid():N}@test.local";
    private readonly string _petraEmail = $"petra-{Guid.NewGuid():N}@test.local";
    private int _janOrder;
    private int _petraOrder;

    public async Task InitializeAsync()
    {
        await web.CreateUserAsync(_janEmail, Password);
        await web.CreateUserAsync(_petraEmail, Password);
        _janOrder = await web.CreateOrderForAsync(_janEmail);
        _petraOrder = await web.CreateOrderForAsync(_petraEmail);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seznam_ZakaznikVidiJenSve()
    {
        var jan = await web.LoginAsync(_janEmail, Password);

        var orders = await jan.GetFromJsonAsync<List<OrderDto>>("/api/orders");
        var page = WebUtility.HtmlDecode(await jan.GetStringAsync("/Orders"));

        Assert.Equal([_janOrder], orders!.Select(o => o.Id));
        Assert.Contains($"Objednávka č. {_janOrder}", page);
        Assert.DoesNotContain($"Objednávka č. {_petraOrder}", page);
        Assert.Contains("Moje objednávky", page);
    }

    [Fact]
    public async Task CiziObjednavka_DetailIZruseni_Vrati404()
    {
        var jan = await web.LoginAsync(_janEmail, Password);

        var detail = await jan.GetAsync($"/api/orders/{_petraOrder}");
        var cancel = await jan.PostAsync($"/api/orders/{_petraOrder}/cancel", null);
        var own = await jan.GetAsync($"/api/orders/{_janOrder}");

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task CiziObjednavka_ZruseniNaStrance_Nenajde()
    {
        var jan = await web.LoginAsync(_janEmail, Password);
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(jan, "/Orders");

        var response = await jan.PostAsync($"/Orders?handler=Cancel&id={_petraOrder}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        // Stránka zobrazí chybu „nebyla nalezena“ (200 s hláškou), objednávka zůstane beze změny.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("nebyla nalezena", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        var petras = await admin.GetFromJsonAsync<OrderDto>($"/api/orders/{_petraOrder}");
        Assert.Equal("Pending", petras!.Status);
    }

    /// <summary>E-shop krok 5: stránka detailu – vlastní objednávka s historií, cizí 404 (číslo v adrese nic neprozradí).</summary>
    [Fact]
    public async Task DetailNaStrance_VlastniSHistorii_CiziNenajde()
    {
        var jan = await web.LoginAsync(_janEmail, Password);

        var own = await jan.GetAsync($"/Orders/{_janOrder}");
        var foreign = await jan.GetAsync($"/Orders/{_petraOrder}");
        var missing = await jan.GetAsync("/Orders/999999");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var html = WebUtility.HtmlDecode(await own.Content.ReadAsStringAsync());
        Assert.Contains($"Objednávka č. {_janOrder}", html);
        Assert.Contains("Objednávka přijata, ověřujeme sklad", html);
        Assert.DoesNotContain("Průběh zadání", html);   // Journeys jen pro admina
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);   // cizí a neexistující vypadají stejně

        var list = WebUtility.HtmlDecode(await jan.GetStringAsync("/Orders"));
        Assert.Contains($"href=\"/Orders/{_janOrder}\"", list);
    }

    [Fact]
    public async Task DetailNaStrance_AdminVidiCiziIPrubeh()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var html = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Orders/{_petraOrder}"));

        Assert.Contains($"Objednávka č. {_petraOrder}", html);
        Assert.Contains("Průběh zadání", html);
    }

    [Fact]
    public async Task Admin_VidiVsechnyObjednavky()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var orders = await admin.GetFromJsonAsync<List<OrderDto>>("/api/orders");
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Orders"));

        Assert.Contains(_janOrder, orders!.Select(o => o.Id));
        Assert.Contains(_petraOrder, orders!.Select(o => o.Id));
        Assert.Contains("Všechny objednávky", page);
    }
}
