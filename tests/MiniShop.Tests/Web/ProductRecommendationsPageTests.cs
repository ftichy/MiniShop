using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Ordering;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 27B: oddíl „Zákazníci také koupili“ na detailu produktu – z kopie katalogu v Orderingu (Reporting se za běhu
/// neptá). Stažený produkt se vynechá, bez doporučení se oddíl neukáže.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class ProductRecommendationsPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task DetailProduktu_UkazeDoporuceneVPoradi_BezStazenych()
    {
        await web.EnsureCatalogProductAsync(9301, "Doporučení – foťák", 9_990m);
        await web.EnsureCatalogProductAsync(9302, "Doporučení – paměťová karta", 399m);
        await web.EnsureCatalogProductAsync(9303, "Doporučení – stažená brašna", 599m);
        await web.EnsureCatalogProductAsync(9304, "Doporučení – stativ", 899m);
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            (await db.CatalogProducts.FindAsync(9301))!.ApplyRecommendations([9304, 9303, 9302], DateTime.UtcNow);
            (await db.CatalogProducts.FindAsync(9303))!.MarkRemoved(DateTime.UtcNow.AddMinutes(1));
            await db.SaveChangesAsync();
        }

        var browser = web.CreateBrowser();
        var response = await browser.GetAsync("/Shop/9301");
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-track=\"product-recommendations\"", page);
        var recommendations = page[page.IndexOf("data-track=\"product-recommendations\"", StringComparison.Ordinal)..];
        Assert.True(recommendations.IndexOf("stativ", StringComparison.Ordinal) < recommendations.IndexOf("paměťová karta", StringComparison.Ordinal),
            "pořadí od Reportingu (nejčastější první)");
        Assert.DoesNotContain("stažená brašna", page);

        var withoutRecommendations = WebUtility.HtmlDecode(await (await browser.GetAsync("/Shop/9302")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("data-track=\"product-recommendations\"", withoutRecommendations);
    }
}
