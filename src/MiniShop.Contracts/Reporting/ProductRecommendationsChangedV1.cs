namespace MiniShop.Contracts.Reporting;

/// <summary>
/// E-shop krok 27A: změnila se doporučení „Zákazníci také koupili“ u produktu – Reporting je spočítal z objednávek,
/// které si zákazníci nechali (dávka každých pár minut). Posílá se jen při skutečné změně seznamu. Odběratel (Ordering
/// v kroku 27B) si ho uloží do kopie katalogu, takže výloha se na Reporting za běhu neptá.
/// </summary>
/// <remarks>
/// Odvozený fakt, ne objednávka: žádné údaje o zákaznících ani o jednotlivých nákupech – dvojice se počítá až od
/// několika různých objednávek. Prázdný seznam = produkt už nic nedoporučuje (smazat).
/// </remarks>
/// <param name="RecommendedProductIds">Nejvýš 4 produkty, nejčastější první.</param>
/// <param name="ComputedAtUtc">Kdy dávka doporučení spočítala – starší výpočet nesmí přepsat novější.</param>
public sealed record ProductRecommendationsChangedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int ProductId,
    IReadOnlyList<int> RecommendedProductIds,
    DateTime ComputedAtUtc) : IIntegrationEvent;
