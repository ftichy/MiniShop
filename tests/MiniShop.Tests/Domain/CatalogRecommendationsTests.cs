using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 27B: doporučení v kopii katalogu – starší výpočet nepřepíše novější, stejně starý se přijme.</summary>
public sealed class CatalogRecommendationsTests
{
    private static readonly DateTime At = new(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StarsiVypocet_NovejsiNeprepise_StejneStaryAPrazdnySeznamAno()
    {
        var product = new CatalogProduct(1, "Foťák", 5_000m, At);

        Assert.True(product.ApplyRecommendations([2, 3], At));
        Assert.False(product.ApplyRecommendations([9], At.AddMinutes(-5)));   // zpráva mimo pořadí
        Assert.Equal([2, 3], product.RecommendedProductIds);

        Assert.True(product.ApplyRecommendations([2, 3], At));                // znovuposlání na žádost Orderingu
        Assert.True(product.ApplyRecommendations([], At.AddMinutes(5)));      // nic nedoporučovat
        Assert.Empty(product.RecommendedProductIds);
    }

    [Fact]
    public void Doporuceni_NikdySamSebe()
    {
        var product = new CatalogProduct(1, "Foťák", 5_000m, At);

        product.ApplyRecommendations([1, 2, 2], At);

        Assert.Equal([2], product.RecommendedProductIds);
    }
}
