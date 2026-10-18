namespace MiniShop.Application.Journeys;

/// <summary>Sledování průběhu zakládání objednávky (krok 12A).</summary>
public interface IJourneyService
{
    /// <summary>Uloží dávku událostí z prohlížeče. Už uložené události (stejné EventId) přeskočí.</summary>
    Task RecordAsync(Guid journeyId, IReadOnlyList<RecordJourneyEventRequest> events, CancellationToken cancellationToken = default);

    /// <summary>Server formulář odmítl – zapíše chybové hlášky do proudu.</summary>
    Task RecordValidationFailedAsync(Guid journeyId, long offsetMs, string errors, CancellationToken cancellationToken = default);

    /// <summary>Objednávka vznikla – spojí proud s jejím číslem.</summary>
    Task RecordOrderSubmittedAsync(Guid journeyId, long offsetMs, int orderId, CancellationToken cancellationToken = default);

    /// <summary>Průběh zakládání objednávky (projekce proudu). NotFoundException, když pro objednávku proud není.</summary>
    Task<OrderJourneyDto> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
}
