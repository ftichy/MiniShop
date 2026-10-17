using System.Net;
using System.Text;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Inventory;

namespace MiniShop.Tests.Web;

/// <summary>
/// Krok 10B: Web mluví se skladem přes HTTP. Klient musí odpovědi API převést zpět na výjimky, které stránky znají –
/// jinak by z "produkt nenalezen" byla chyba 500 a ze spadlé služby skladu spadlá stránka.
/// Server tu nahrazuje falešný HttpMessageHandler, takže test nepotřebuje síť ani běžící Inventory.Api.
/// </summary>
public sealed class InventoryApiClientTests
{
    [Fact]
    public async Task Uspech_VratiProdukty()
    {
        var client = Client(_ => Json(HttpStatusCode.OK,
            """[{"id":1,"name":"Myš","description":null,"price":890.0,"stockQuantity":40}]"""));

        var products = await client.GetAllAsync();

        Assert.Equal(new ProductDto(1, "Myš", null, 890m, 40), Assert.Single(products));
    }

    [Fact]
    public async Task Pozadavek_JdeNaSpravnouAdresuAMetodou()
    {
        HttpRequestMessage? sent = null;
        var client = Client(request =>
        {
            sent = request;
            return Json(HttpStatusCode.OK, """{"id":3,"name":"Kabel","price":199.0,"stockQuantity":25}""");
        });

        await client.AddStockAsync(3, 5);

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("http://inventory/api/products/3/stock", sent.RequestUri!.ToString());
    }

    [Fact]
    public async Task Chyba404_JeNotFoundException()
    {
        var client = Client(_ => Json(HttpStatusCode.NotFound,
            """{"status":404,"title":"Nenalezeno","detail":"Produkt 99 neexistuje."}"""));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => client.GetByIdAsync(99));
        Assert.Equal("Produkt 99 neexistuje.", ex.Message);
    }

    [Fact]
    public async Task Chyba400_JeDomainException()
    {
        var client = Client(_ => Json(HttpStatusCode.BadRequest,
            """{"status":400,"title":"Neplatná operace","detail":"Množství musí být kladné."}"""));

        var ex = await Assert.ThrowsAsync<DomainException>(() => client.AddStockAsync(1, 5));
        Assert.Equal("Množství musí být kladné.", ex.Message);
    }

    [Fact]
    public async Task ValidacniChyba_JeDomainExceptionSeSeznamemChyb()
    {
        var client = Client(_ => Json(HttpStatusCode.BadRequest,
            """{"status":400,"title":"One or more validation errors occurred.","errors":{"Name":["Název je povinný."]}}"""));

        var ex = await Assert.ThrowsAsync<DomainException>(() => client.CreateAsync(new CreateProductRequest()));
        Assert.Equal("Název je povinný.", ex.Message);
    }

    [Fact]
    public async Task Chyba500_JeInventoryUnavailable()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<InventoryUnavailableException>(() => client.GetAllAsync());
    }

    [Fact]
    public async Task SluzbaNebezi_JeInventoryUnavailable()
    {
        var client = Client(_ => throw new HttpRequestException("Connection refused"));

        var ex = await Assert.ThrowsAsync<InventoryUnavailableException>(() => client.GetAllAsync());
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    private static InventoryApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)) { BaseAddress = new Uri("http://inventory/") });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
