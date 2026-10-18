namespace MiniShop.Application.Journeys;

/// <summary>
/// Jedna událost z prohlížeče (order-form-tracker.js). Type je text (FieldChanged, Clicked…) – převede ho služba,
/// neznámý typ = chyba 400.
/// </summary>
public sealed class RecordJourneyEventRequest
{
    public Guid EventId { get; set; }
    public string Type { get; set; } = string.Empty;
    public long OffsetMs { get; set; }
    public string? Target { get; set; }
    public string? Label { get; set; }
    public string? Value { get; set; }
    public string? ValueText { get; set; }
}

/// <summary>Uložená událost tak, jak se stala – podklad pro přehrání (krok 12B).</summary>
public sealed record JourneyEventDto(
    Guid EventId,
    string Type,
    long OffsetMs,
    string? Target,
    string? Label,
    string? Value,
    string? ValueText);

/// <summary>Krok časové osy pro člověka. Víc událostí může splynout do jednoho kroku (psaní jména = jeden krok).</summary>
/// <param name="OffsetMs">Začátek kroku od otevření formuláře.</param>
/// <param name="EndOffsetMs">Konec kroku (u psaní poslední napsaný znak), jinak stejný jako začátek.</param>
/// <param name="Kind">Typ první události kroku (FieldChanged, Clicked…) – pro ikonu nebo barvu v UI.</param>
public sealed record JourneyStepDto(long OffsetMs, long EndOffsetMs, string Kind, string Description);

/// <summary>Kolik času strávil uživatel v poli (součet všech vstupů do pole).</summary>
public sealed record JourneyFieldTimeDto(string Label, long DurationMs);

/// <summary>Souhrn proudu.</summary>
/// <param name="DurationMs">Od otevření formuláře po vznik objednávky (nebo poslední událost).</param>
/// <param name="ChangesByField">Kolikrát se pole měnilo (souvislé psaní = jedna změna).</param>
public sealed record JourneySummaryDto(
    long DurationMs,
    int ClickCount,
    int FailedSubmits,
    IReadOnlyDictionary<string, int> ChangesByField,
    IReadOnlyList<JourneyFieldTimeDto> TimeInFields);

/// <summary>Průběh zakládání objednávky – výsledek projekce <see cref="OrderJourneyProjection"/>.</summary>
public sealed record OrderJourneyDto(
    int OrderId,
    Guid JourneyId,
    DateTime StartedAtUtc,
    JourneySummaryDto Summary,
    IReadOnlyList<JourneyStepDto> Steps,
    IReadOnlyList<JourneyEventDto> Events);
