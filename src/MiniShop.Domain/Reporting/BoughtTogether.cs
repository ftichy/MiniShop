namespace MiniShop.Domain.Reporting;

/// <summary>
/// E-shop krok 27A: výpočet „Zákazníci také koupili“ – které produkty se nejčastěji objevují ve stejné objednávce.
/// Přirovnání: prodavač, který si pamatuje, že kdo kupuje foťák, většinou chce i paměťovou kartu.
/// </summary>
/// <remarks>
/// Košík = různé produkty jedné objednávky (počet kusů nehraje roli). Dvojice se počítá až od <see cref="MinOrders"/>
/// různých objednávek – z doporučení tak nejde vyčíst, co si koupil jeden konkrétní člověk. Pořadí: nejčastější první,
/// při shodě nižší ID (stejná data = vždy stejný výsledek, jinak by se zprávy posílaly zbytečně).
/// </remarks>
public static class BoughtTogether
{
    /// <summary>Kolik různých objednávek musí dvojici obsahovat, aby se doporučila.</summary>
    public const int MinOrders = 2;

    /// <summary>Kolik doporučení nejvýš na produkt.</summary>
    public const int MaxRecommendations = 4;

    /// <returns>Pro každý produkt (jen s aspoň jedním doporučením) seřazená ID doporučených produktů.</returns>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> Compute(IEnumerable<IEnumerable<int>> baskets)
    {
        var pairs = new Dictionary<(int Product, int Other), int>();
        foreach (var basket in baskets)
        {
            var products = basket.Distinct().ToList();
            foreach (var product in products)
            {
                foreach (var other in products.Where(other => other != product))
                {
                    pairs[(product, other)] = pairs.GetValueOrDefault((product, other)) + 1;
                }
            }
        }

        return pairs
            .Where(pair => pair.Value >= MinOrders)
            .GroupBy(pair => pair.Key.Product)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key.Other)
                    .Take(MaxRecommendations)
                    .Select(pair => pair.Key.Other)
                    .ToList());
    }
}

/// <summary>
/// E-shop krok 27A: naposledy odeslaná doporučení produktu – podle nich dávka pozná, co se změnilo, a pošle zprávu
/// jen tehdy (jinak by každých 5 minut odešla zpráva za každý produkt).
/// </summary>
public sealed class ProductRecommendation
{
    private List<int> _recommendedProductIds = [];

    // Konstruktor pro EF Core.
    private ProductRecommendation()
    {
    }

    public int ProductId { get; private set; }

    public IReadOnlyList<int> RecommendedProductIds => _recommendedProductIds.AsReadOnly();

    public DateTime ComputedAtUtc { get; private set; }

    public static ProductRecommendation Create(int productId, IReadOnlyList<int> recommended, DateTime computedAtUtc) =>
        new() { ProductId = productId, _recommendedProductIds = [.. recommended], ComputedAtUtc = computedAtUtc };

    /// <summary>Nový výsledek dávky. Vrací false, když se seznam nezměnil (nic se neposílá).</summary>
    public bool Update(IReadOnlyList<int> recommended, DateTime computedAtUtc)
    {
        if (_recommendedProductIds.SequenceEqual(recommended))
        {
            return false;
        }

        _recommendedProductIds = [.. recommended];
        ComputedAtUtc = computedAtUtc;
        return true;
    }
}
