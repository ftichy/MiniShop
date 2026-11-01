using System.Net;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 2B: veřejné obrázky <c>/media/products/{soubor}</c> – Web je bere ze skladu (tady <see cref="StubProductImageService"/>)
/// a posílá je s cache „navždy“, protože jméno souboru se s obsahem mění.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class ProductImageEndpointTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task ZnamyObrazek_BezPrihlaseni_SCacheNaRok()
    {
        var response = await web.CreateBrowser().GetAsync("/media/products/" + StubProductImageService.KnownFileName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StubProductImageService.KnownContent, await response.Content.ReadAsByteArrayAsync());
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.Public);
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
        Assert.Contains("immutable", cache.Extensions.Select(e => e.Name));
    }

    [Theory]
    [InlineData("1-ffffffffffff.png")]     // platné jméno, sklad ho nezná
    [InlineData("..%2F..%2Fappsettings.json")]
    [InlineData("1-0123456789ab.svg")]
    public async Task NeznamyNeboNeplatny_404(string fileName)
    {
        var response = await web.CreateBrowser().GetAsync("/media/products/" + fileName);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
