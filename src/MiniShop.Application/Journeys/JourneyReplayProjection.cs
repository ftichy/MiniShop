namespace MiniShop.Application.Journeys;

/// <summary>Jedna událost připravená k přehrání.</summary>
/// <param name="PlayAtMs">Kdy ji přehrávač odehraje (od začátku přehrávání, po zkrácení dlouhých pauz).</param>
/// <param name="SkippedPauseMs">O kolik se pauza před událostí zkrátila (0 = pauza odpovídá skutečnosti).</param>
/// <param name="StepIndex">Ke kterému kroku časové osy (<see cref="OrderJourneyDto.Steps"/>) událost patří.</param>
public sealed record JourneyReplayFrameDto(long PlayAtMs, long SkippedPauseMs, int StepIndex, JourneyEventDto Event);

/// <summary>Podklad pro přehrávač (krok 12B): průběh z 12A a k němu snímky přehrávání.</summary>
/// <param name="DurationMs">Délka přehrávání (po zkrácení pauz) – skutečná doba je v Journey.Summary.DurationMs.</param>
public sealed record JourneyReplayDto(OrderJourneyDto Journey, long MaxPauseMs, long DurationMs, IReadOnlyList<JourneyReplayFrameDto> Frames);

/// <summary>
/// Druhá projekce stejného proudu (krok 12B): z událostí udělá „scénář“ pro přehrávač – kdy se která akce odehraje.
/// </summary>
/// <remarks>
/// Proud se nemění – stejné události, jen jiný pohled. 12A z nich skládá časovou osu pro čtení, tady časovou osu pro
/// přehrání. Dlouhé pauzy (uživatel odběhl od počítače) se zkrátí na <see cref="DefaultMaxPauseMs"/>, aby se na přehrávání
/// nečekalo minuty; o kolik se zkrátily, nese snímek (přehrávač to ukáže: „uživatel 40 s nic nedělal“).
/// Psaní zůstává ve skutečném tempu – každý znak je samostatná událost s vlastním časem.
/// </remarks>
public static class JourneyReplayProjection
{
    public const long DefaultMaxPauseMs = 3_000;

    public static JourneyReplayDto Project(OrderJourneyDto journey, long maxPauseMs = DefaultMaxPauseMs)
    {
        var frames = new List<JourneyReplayFrameDto>(journey.Events.Count);
        long playAtMs = 0;
        long? previousOffsetMs = null;

        foreach (var e in journey.Events)
        {
            var pauseMs = previousOffsetMs is { } previous ? Math.Max(0, e.OffsetMs - previous) : e.OffsetMs;
            var playedPauseMs = Math.Min(pauseMs, maxPauseMs);

            playAtMs += playedPauseMs;
            frames.Add(new JourneyReplayFrameDto(playAtMs, pauseMs - playedPauseMs, StepIndexOf(journey.Steps, e.OffsetMs), e));
            previousOffsetMs = e.OffsetMs;
        }

        return new JourneyReplayDto(journey, maxPauseMs, playAtMs, frames);
    }

    /// <summary>Poslední krok, který začal nejpozději v čase události (psaní jména = všechny znaky patří k jednomu kroku).</summary>
    private static int StepIndexOf(IReadOnlyList<JourneyStepDto> steps, long offsetMs)
    {
        var index = -1;
        for (var i = 0; i < steps.Count && steps[i].OffsetMs <= offsetMs; i++)
        {
            index = i;
        }

        return index;
    }
}
