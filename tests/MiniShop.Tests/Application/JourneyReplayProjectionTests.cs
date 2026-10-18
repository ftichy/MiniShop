using MiniShop.Application.Journeys;

namespace MiniShop.Tests.Application;

/// <summary>Krok 12B: scénář přehrávání – druhá projekce stejného proudu (zkracování pauz, přiřazení ke krokům).</summary>
public sealed class JourneyReplayProjectionTests
{
    [Fact]
    public void DlouhePauzy_SeZkrati_KratkeZustanou()
    {
        var journey = Journey(
            [Step(0), Step(1_000), Step(41_000)],
            Event("FormOpened", 0),
            Event("FieldFocused", 1_000),
            Event("FieldFocused", 41_000),     // uživatel 40 s nic nedělal
            Event("FieldChanged", 41_200));

        var replay = JourneyReplayProjection.Project(journey, maxPauseMs: 3_000);

        Assert.Equal([0L, 1_000, 4_000, 4_200], replay.Frames.Select(f => f.PlayAtMs));
        Assert.Equal([0L, 0, 37_000, 0], replay.Frames.Select(f => f.SkippedPauseMs));
        Assert.Equal(4_200, replay.DurationMs);
        Assert.Equal(3_000, replay.MaxPauseMs);
    }

    [Fact]
    public void Udalosti_PatriKeKrokuKteryJeZacal()
    {
        // Krok 1 = psaní jména (3 znaky), krok 2 = klik. Blur nemá vlastní krok – patří k poslednímu.
        var journey = Journey(
            [Step(0), Step(1_000), Step(5_000)],
            Event("FormOpened", 0),
            Event("FieldChanged", 1_000),
            Event("FieldChanged", 1_200),
            Event("FieldChanged", 1_400),
            Event("FieldBlurred", 2_000),
            Event("Clicked", 5_000));

        var replay = JourneyReplayProjection.Project(journey);

        Assert.Equal([0, 1, 1, 1, 1, 2], replay.Frames.Select(f => f.StepIndex));
    }

    [Fact]
    public void PrvniUdalost_PozdeOdOtevreni_ZacnePoZkracenePauze()
    {
        // Po neúspěšném odeslání (nebo když se FormOpened neodeslal) může proud začínat později než v čase 0.
        var replay = JourneyReplayProjection.Project(Journey([Step(10_000)], Event("SubmitClicked", 10_000)));

        var frame = Assert.Single(replay.Frames);
        Assert.Equal((3_000L, 7_000L), (frame.PlayAtMs, frame.SkippedPauseMs));
    }

    [Fact]
    public void PrazdnyProud_PrazdnyScenar()
    {
        var replay = JourneyReplayProjection.Project(Journey([]));

        Assert.Empty(replay.Frames);
        Assert.Equal(0, replay.DurationMs);
    }

    // ---------- Pomocné metody ----------

    private static OrderJourneyDto Journey(IReadOnlyList<JourneyStepDto> steps, params JourneyEventDto[] events) =>
        new(1, Guid.NewGuid(), DateTime.UtcNow,
            new JourneySummaryDto(0, 0, 0, new Dictionary<string, int>(), []),
            steps, events);

    private static JourneyStepDto Step(long offsetMs) => new(offsetMs, offsetMs, "FieldChanged", $"krok v {offsetMs}");

    private static JourneyEventDto Event(string type, long offsetMs) =>
        new(Guid.NewGuid(), type, offsetMs, "Input.CustomerName", "Jméno", null, null);
}
