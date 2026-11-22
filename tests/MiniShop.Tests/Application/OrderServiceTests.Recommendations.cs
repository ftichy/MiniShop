using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Reporting;
using MiniShop.Infrastructure.Ordering;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 27: „Zákazníci také koupili“ celou cestou – objednávky → přehled tržeb (Reporting) → dávka →
/// ProductRecommendationsChangedV1 → kopie katalogu v Orderingu → detail produktu. A doplnění kopie na žádost Orderingu.
/// </summary>
public sealed partial class OrderServiceTests
{
    [Fact]
    public async Task Doporuceni_ZObjednavekDoKopieKatalogu()
    {
        var camera = await AddProductAsync(price: 5_000m, stock: 10);
        var card = await AddProductAsync(price: 300m, stock: 10);
        var tripod = await AddProductAsync(price: 800m, stock: 10);
        await PlaceTogetherAsync(camera.Id, card.Id);
        await PlaceTogetherAsync(camera.Id, card.Id);
        await PlaceTogetherAsync(camera.Id, tripod.Id);   // jen jednou – pod prahem, nedoporučí se

        Assert.True(await Recommendations(s => s.RecomputeAsync()) > 0);
        await ProcessOutboxAsync();   // ProductRecommendationsChangedV1 → CatalogProjectionHandler

        Assert.Equal([card.Id], (await Catalog(s => s.GetRecommendationsAsync(camera.Id))).Select(p => p.ProductId));
        Assert.Equal([camera.Id], (await Catalog(s => s.GetRecommendationsAsync(card.Id))).Select(p => p.ProductId));
        Assert.Empty(await Catalog(s => s.GetRecommendationsAsync(tripod.Id)));
    }

    [Fact]
    public async Task Doporuceni_KopieBezNich_SiJeOrderingVyzadaPoStartu()
    {
        var camera = await AddProductAsync(price: 5_000m, stock: 10);
        var card = await AddProductAsync(price: 300m, stock: 10);
        await PlaceTogetherAsync(camera.Id, card.Id);
        await PlaceTogetherAsync(camera.Id, card.Id);
        await Recommendations(s => s.RecomputeAsync());
        await ProcessOutboxAsync();

        // Kopie katalogu o doporučení přišla (e-shop krok 27A: zprávy nikdo neposlouchal; nová databáze).
        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().CatalogProducts.ExecuteUpdateAsync(s => s
                .SetProperty(p => EF.Property<List<int>>(p, "_recommendedProductIds"), new List<int>())
                .SetProperty(p => p.RecommendationsComputedAtUtc, (DateTime?)null));
        }

        Assert.Empty(await Catalog(s => s.GetRecommendationsAsync(camera.Id)));

        Assert.True(await CatalogSync(s => s.RequestRecommendationsAsync()));   // jako CatalogSyncOnStartupWorker
        await ProcessOutboxAsync();   // SyncProductRecommendationsV1 → Reporting pošle uložená doporučení znovu

        Assert.Equal([card.Id], (await Catalog(s => s.GetRecommendationsAsync(camera.Id))).Select(p => p.ProductId));
    }

    private async Task PlaceTogetherAsync(params int[] productIds)
    {
        await Orders(s => s.PlaceOrderAsync(CreateRequest([.. productIds.Select(id => (id, 1))]), Jan));
        await ProcessOutboxAsync();   // sága potvrdí, Reporting si objednávku zapíše do přehledu tržeb
    }

    private async Task<T> Recommendations<T>(Func<ProductRecommendationService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ProductRecommendationService>());
    }
}
