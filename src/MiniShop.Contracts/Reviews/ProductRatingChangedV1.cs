namespace MiniShop.Contracts.Reviews;

/// <summary>
/// E-shop krok 14: změnilo se hodnocení produktu (nové, upravené nebo skryté hodnocení) – souhrn pro kohokoli, kdo ho
/// chce ukazovat. Ordering si ho uloží do kopie katalogu, takže výloha hvězdičky ukáže bez dotazu do modulu Reviews.
/// </summary>
/// <param name="AverageRating">Průměr viditelných hodnocení (1–5, na desetiny), 0 = žádné.</param>
/// <param name="RatingCount">Počet viditelných hodnocení.</param>
public sealed record ProductRatingChangedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int ProductId,
    decimal AverageRating,
    int RatingCount) : IIntegrationEvent;
