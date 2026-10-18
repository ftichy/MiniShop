using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Journeys;

namespace MiniShop.Application.Journeys;

/// <summary>
/// Modul Journeys (krok 12A): zapisuje, co uživatel dělá ve formuláři objednávky, a skládá z toho průběh (projekci).
/// </summary>
/// <remarks>
/// Zápis je idempotentní podle EventId: prohlížeč pošle dávku znovu, když první pokus selhal (nebo odejde dvakrát –
/// fetch i sendBeacon při opuštění stránky), a nic se nezdvojí. Stejný princip jako inbox z kroku 6.
/// Ordering o modulu neví: spojení s objednávkou zapíše stránka objednávek (<see cref="RecordOrderSubmittedAsync"/>).
/// </remarks>
public sealed class JourneyService(
    IJourneyEventRepository events,
    IJourneysUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJourneyService
{
    /// <summary>Víc událostí najednou prohlížeč neposílá (dávka za ~2 s) – ochrana před zahlcením.</summary>
    public const int MaxBatchSize = 200;

    public async Task RecordAsync(Guid journeyId, IReadOnlyList<RecordJourneyEventRequest> batch,
        CancellationToken cancellationToken = default)
    {
        if (batch.Count > MaxBatchSize)
        {
            throw new DomainException($"Dávka může mít nejvýš {MaxBatchSize} událostí.");
        }

        var unique = batch.DistinctBy(e => e.EventId).ToList();
        var existing = await events.GetExistingEventIdsAsync(unique.Select(e => e.EventId).ToList(), cancellationToken);

        var now = UtcNow;
        var newEvents = unique
            .Where(e => !existing.Contains(e.EventId))
            .Select(e => new JourneyEvent(e.EventId, journeyId, ParseClientType(e.Type), e.OffsetMs,
                e.Target, e.Label, e.Value, e.ValueText, now))
            .ToList();

        if (newEvents.Count == 0)
        {
            return;   // celá dávka už byla uložená dřív
        }

        events.AddRange(newEvents);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordValidationFailedAsync(Guid journeyId, long offsetMs, string errors,
        CancellationToken cancellationToken = default)
    {
        events.AddRange([JourneyEvent.ValidationFailed(journeyId, offsetMs, errors, UtcNow)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordOrderSubmittedAsync(Guid journeyId, long offsetMs, int orderId,
        CancellationToken cancellationToken = default)
    {
        TraceTags.TagOrder(orderId);
        events.AddRange([JourneyEvent.OrderSubmitted(journeyId, offsetMs, orderId, UtcNow)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrderJourneyDto> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        TraceTags.TagOrder(orderId);

        var journeyId = await events.FindJourneyIdByOrderIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException(
                $"Pro objednávku č. {orderId} není záznam průběhu (vznikla přes API, před krokem 12, nebo bez JavaScriptu).");

        var journey = await events.GetJourneyAsync(journeyId, cancellationToken);
        return OrderJourneyProjection.Project(orderId, journeyId, journey);
    }

    /// <summary>Prohlížeč smí poslat jen „své“ typy – číslo objednávky nebo chybu validace zapisuje jen server.</summary>
    private static JourneyEventType ParseClientType(string type)
    {
        if (!Enum.TryParse<JourneyEventType>(type, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new DomainException($"Neznámý typ události „{type}“.");
        }

        if (JourneyEvent.IsServerOnly(parsed))
        {
            throw new DomainException($"Událost {parsed} smí zapsat jen server.");
        }

        return parsed;
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
