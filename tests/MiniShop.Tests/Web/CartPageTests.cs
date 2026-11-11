using System.Net;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 3: košík přes skutečné formuláře – pole posílaná stejně jako z prohlížeče (výloha → Košík).
/// Produkty jsou rovnou v kopii katalogu (<see cref="MiniShopWebFactory.EnsureCatalogProductAsync"/>);
/// každý test má vlastní ID produktu, protože zákazník je pro celou kolekci testů stejný.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class CartPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task PridaniZVylohy_VratiNaVylohuSHlaskouAJeVKosiku()
    {
        await web.EnsureCatalogProductAsync(301, "Košíková myš", 250m);
        var client = await CustomerAsync();

        var shop = await client.GetStringAsync("/Shop?q=kosikova");
        Assert.Contains("data-track=\"shop-add-to-cart\"", shop);

        var response = await PostAsync(client, "/Cart?handler=Add", ("productId", "301"), ("returnUrl", "/Shop?q=kosikova"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Shop?q=kosikova", response.Headers.Location?.OriginalString);
        Assert.Contains("„Košíková myš“ je v košíku.", Decode(await client.GetStringAsync("/Shop?q=kosikova")));
        var cart = Decode(await client.GetStringAsync("/Cart"));
        Assert.Contains("Košíková myš", cart);
        Assert.Contains("data-track=\"nav-cart\"", cart);   // odkaz s počtem kusů v hlavičce
    }

    /// <summary>
    /// Oprava po nasazení: prohlížeč držel starý site.css v cache (bez .cart-thumb) a košík ukázal obrázky v plné
    /// velikosti. Adresa stylu proto nese otisk obsahu a náhled má rozměr i v HTML.
    /// </summary>
    [Fact]
    public async Task NahledVKosiku_MalyIBezNovehoCss()
    {
        await web.EnsureCatalogProductAsync(305, "Myš s obrázkem", 300m, StubProductImageService.KnownFileName);
        var client = await CustomerAsync();
        await PostAsync(client, "/Cart?handler=Add", ("productId", "305"));

        var html = await client.GetStringAsync("/Cart");

        Assert.Matches("href=\"/css/site\\.css\\?v=[^\"]+\"", html);
        Assert.Matches("class=\"cart-thumb\"\\s+width=\"96\" height=\"72\"", html);   // vzhled B: větší náhled v kartě
    }

    [Fact]
    public async Task CiziAdresaNavratu_ZustaneNaKosiku()
    {
        await web.EnsureCatalogProductAsync(302, "Kabel do košíku", 90m);
        var client = await CustomerAsync();

        var response = await PostAsync(client, "/Cart?handler=Add", ("productId", "302"), ("returnUrl", "https://evil.example/"));

        Assert.Equal("/Cart", response.Headers.Location?.OriginalString);   // žádné přesměrování ven (open redirect)
    }

    [Fact]
    public async Task ZmenaMnozstviMimoRozsah_HlaskaNaStrance()
    {
        await web.EnsureCatalogProductAsync(303, "Podložka", 120m);
        var client = await CustomerAsync();
        await PostAsync(client, "/Cart?handler=Add", ("productId", "303"));

        var response = await PostAsync(client, "/Cart?handler=ChangeQuantity", ("productId", "303"), ("quantity", "500"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Množství musí být od 1 do 100 kusů.", Decode(await client.GetStringAsync("/Cart")));
    }

    [Fact]
    public async Task Odebrani_ProduktZmiziZKosiku()
    {
        await web.EnsureCatalogProductAsync(304, "Odebíraný reproduktor", 990m);
        var client = await CustomerAsync();
        await PostAsync(client, "/Cart?handler=Add", ("productId", "304"));

        await PostAsync(client, "/Cart?handler=Remove", ("productId", "304"));

        var page = Decode(await client.GetStringAsync("/Cart"));
        Assert.DoesNotContain("name=\"productId\" value=\"304\"", page);   // řádek v košíku už není
        Assert.Contains("Odebráno: Odebíraný reproduktor", page);          // e-shop krok 17: ale v historii košíku ano
    }

    [Fact]
    public async Task Admin_VHlavickceKosikNema()
    {
        var client = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        Assert.DoesNotContain("data-track=\"nav-cart\"", await client.GetStringAsync("/Shop"));
    }

    private Task<HttpClient> CustomerAsync() => web.LoginAsync(MiniShopWebFactory.UserEmail, MiniShopWebFactory.UserPassword);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, params (string Name, string Value)[] fields)
    {
        var form = fields.ToDictionary(f => f.Name, f => f.Value);
        form["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Shop");
        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html);
}
