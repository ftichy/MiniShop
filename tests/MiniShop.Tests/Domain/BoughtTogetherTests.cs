using MiniShop.Domain.Reporting;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 27A: výpočet „Zákazníci také koupili“ z košíků objednávek.</summary>
public sealed class BoughtTogetherTests
{
    [Fact]
    public void Dvojice_ZJedineObjednavky_SeNedoporuci()
    {
        // Jedna objednávka = jeden člověk – z doporučení by šlo vyčíst, co si koupil.
        var result = BoughtTogether.Compute([[1, 2]]);

        Assert.Empty(result);
    }

    [Fact]
    public void Doporuceni_NejcastejsiPrvni_PriShodeNizsiId_OboustranneAKusyNehrajiRoli()
    {
        var result = BoughtTogether.Compute(
        [
            [1, 2], [1, 2], [1, 2, 2, 2],   // 1+2 ve třech objednávkách (víc kusů 2 = pořád jedna objednávka)
            [1, 4], [1, 4],
            [1, 3], [1, 3]
        ]);

        Assert.Equal([2, 3, 4], result[1]);   // 3 a 4 shodně dvakrát → nižší ID první
        Assert.Equal([1], result[2]);
        Assert.Equal([1], result[3]);
    }

    [Fact]
    public void Doporuceni_NejvysCtyri()
    {
        var baskets = Enumerable.Range(0, 2).Select(_ => (IEnumerable<int>)[1, 2, 3, 4, 5, 6]).ToList();

        var result = BoughtTogether.Compute(baskets);

        Assert.Equal([2, 3, 4, 5], result[1]);
    }

    [Fact]
    public void Ulozena_Doporuceni_ZmenuPoznaJenPriJinemSeznamu()
    {
        var at = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
        var recommendation = ProductRecommendation.Create(1, [2, 3], at);

        Assert.False(recommendation.Update([2, 3], at.AddMinutes(5)));
        Assert.Equal(at, recommendation.ComputedAtUtc);
        Assert.True(recommendation.Update([3, 2], at.AddMinutes(10)));   // pořadí je význam
        Assert.Equal([3, 2], recommendation.RecommendedProductIds);
    }
}
