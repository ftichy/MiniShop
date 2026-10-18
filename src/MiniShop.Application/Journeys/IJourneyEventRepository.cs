using MiniShop.Domain.Journeys;

namespace MiniShop.Application.Journeys;

/// <summary>
/// Úložiště proudů událostí (krok 12) – „event store“ v malém. Události jen přibývají: žádné Update ani Delete.
/// </summary>
public interface IJourneyEventRepository
{
    /// <summary>Které z daných EventId už jsou uložené (opakovaně poslaná dávka z prohlížeče).</summary>
    Task<IReadOnlySet<Guid>> GetExistingEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);

    /// <summary>Proud, ve kterém vznikla objednávka (podle události OrderSubmitted); null = žádný.</summary>
    Task<Guid?> FindJourneyIdByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Celý proud seřazený podle času od otevření formuláře.</summary>
    Task<IReadOnlyList<JourneyEvent>> GetJourneyAsync(Guid journeyId, CancellationToken cancellationToken = default);

    void AddRange(IEnumerable<JourneyEvent> events);
}
