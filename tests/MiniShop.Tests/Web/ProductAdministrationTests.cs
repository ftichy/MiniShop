using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Inventory;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 2A: formuláře kategorií na stránce Produkty – odeslané stejně jako z prohlížeče
/// (pole <c>category.Name</c>). Sklad je tu <see cref="StubCategoryService"/>.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class ProductAdministrationTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task NovaKategorie_DojdeDoSkladuAPresmeruje()
    {
        var response = await PostAsync("/Products?handler=CreateCategory", ("category.Name", "Periferie"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Periferie", Stub.ReceivedNames);
    }

    [Fact]
    public async Task PrejmenovaniKategorie_DojdeDoSkladuAPresmeruje()
    {
        var response = await PostAsync("/Products?handler=RenameCategory&id=1", ("category.Name", "Příslušenství"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Příslušenství", Stub.ReceivedNames);
    }

    [Fact]
    public async Task PrazdnyNazevKategorie_ChybaNaStrance()
    {
        var response = await PostAsync("/Products?handler=CreateCategory", ("category.Name", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // stránka znovu s chybou, nic se neposlalo
        Assert.Contains("Název kategorie je povinný.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>E-shop krok 2B: formulář obrázku neposílá pole produktu – nesmí kvůli nim „neprojít validací“.</summary>
    [Fact]
    public async Task NahraniObrazku_DojdeDoSkladuAPresmeruje()
    {
        var client = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        var form = new MultipartFormDataContent
        {
            { new StringContent(await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Products/Edit/1")), "__RequestVerificationToken" },
            { new ByteArrayContent(StubProductImageService.KnownContent), "image", "mys.png" }
        };

        var response = await client.PostAsync("/Products/Edit/1?handler=Image", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(ImageStub.Uploaded, content => content.SequenceEqual(StubProductImageService.KnownContent));
    }

    [Fact]
    public async Task UpravaProduktu_UkazeNahledObrazku()
    {
        var client = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var html = await client.GetStringAsync("/Products/Edit/1");

        Assert.Contains("/media/products/" + StubProductImageService.KnownFileName, html);
    }

    private StubCategoryService Stub => (StubCategoryService)web.Services.GetRequiredService<ICategoryService>();

    private StubProductImageService ImageStub =>
        (StubProductImageService)web.Services.GetRequiredService<IProductImageService>();

    private async Task<HttpResponseMessage> PostAsync(string url, params (string Name, string Value)[] fields)
    {
        var client = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        var form = fields.ToDictionary(f => f.Name, f => f.Value);
        form["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Products");
        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }
}
