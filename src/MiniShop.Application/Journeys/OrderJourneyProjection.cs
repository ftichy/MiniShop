using MiniShop.Domain.Journeys;

namespace MiniShop.Application.Journeys;

/// <summary>
/// Projekce (krok 12A): z proudu událostí složí čitelný průběh zakládání objednávky – časovou osu a souhrn.
/// </summary>
/// <remarks>
/// <b>Projekce = „fold“.</b> Začne s prázdným stavem a pro každou událost v pořadí zavolá <see cref="Apply"/>, která stav
/// posune dál. Jako když počítáte zůstatek účtu z výpisu pohybů: neukládáte zůstatek, ale umíte ho kdykoli spočítat.
/// Projekce nic neukládá a může se kdykoli změnit (nový souhrn, jiné slučování kroků) – proud zůstane stejný a starý
/// průběh se spočítá novým způsobem. Stejný proud promítá i přehrávání (krok 12B), jen jinak.
/// Kopie katalogu v Orderingu (CatalogProjectionHandler) je také projekce, jen uložená do tabulky.
/// </remarks>
public sealed class OrderJourneyProjection
{
    private readonly List<StepBuilder> _steps = [];
    private readonly Dictionary<string, int> _changesByField = [];
    private readonly Dictionary<string, long> _timeInFields = [];
    private (string Label, long FocusedAtMs)? _focusedField;
    private int _clickCount;
    private int _failedSubmits;
    private long _lastOffsetMs;
    private long? _submittedAtMs;

    /// <summary>Celý fold najednou: nový stav → Apply pro každou událost → výsledek.</summary>
    public static OrderJourneyDto Project(int orderId, Guid journeyId, IReadOnlyList<JourneyEvent> events)
    {
        var projection = new OrderJourneyProjection();
        foreach (var journeyEvent in events.OrderBy(e => e.OffsetMs).ThenBy(e => e.Id))
        {
            projection.Apply(journeyEvent);
        }

        var startedAtUtc = events.Count == 0
            ? DateTime.MinValue
            : events.Min(e => e.RecordedAtUtc.AddMilliseconds(-e.OffsetMs));

        return new OrderJourneyDto(orderId, journeyId, startedAtUtc, projection.Summary(), projection.Steps(),
            events.OrderBy(e => e.OffsetMs).ThenBy(e => e.Id).Select(ToDto).ToList());
    }

    /// <summary>Jeden krok foldu: posune stav podle jedné události.</summary>
    public void Apply(JourneyEvent e)
    {
        _lastOffsetMs = Math.Max(_lastOffsetMs, e.OffsetMs);
        var label = e.Label ?? e.Target ?? "?";

        switch (e.Type)
        {
            case JourneyEventType.FormOpened:
                AddStep(e, "Otevřel formulář objednávky");
                break;

            case JourneyEventType.FieldFocused:
                CloseFocusedField(e.OffsetMs);
                _focusedField = (label, e.OffsetMs);
                AddStep(e, $"Vstoupil do pole „{label}“");
                break;

            case JourneyEventType.FieldBlurred:
                CloseFocusedField(e.OffsetMs);
                break;

            case JourneyEventType.FieldChanged:
                ApplyChange(e, label);
                break;

            case JourneyEventType.Clicked:
                _clickCount++;
                AddStep(e, $"Klikl na „{label}“");
                break;

            case JourneyEventType.SubmitClicked:
                CloseFocusedField(e.OffsetMs);
                AddStep(e, "Odeslal formulář");
                break;

            case JourneyEventType.ValidationFailed:
                _failedSubmits++;
                AddStep(e, $"Server formulář vrátil: {e.Value}");
                break;

            case JourneyEventType.FieldInvalid:
                // Jeden pokus o odeslání může odmítnout víc polí najednou – neúspěšné odeslání se počítá jednou.
                if (_steps.LastOrDefault()?.Type != JourneyEventType.FieldInvalid)
                {
                    _failedSubmits++;
                }
                AddStep(e, $"Prohlížeč nepustil odeslání – „{label}“: {e.Value}");
                break;

            case JourneyEventType.OrderSubmitted:
                _submittedAtMs = e.OffsetMs;
                AddStep(e, $"Objednávka č. {e.OrderId} vznikla");
                break;
        }
    }

    /// <summary>
    /// Psaní do textového pole = desítky událostí (každý znak). Dokud uživatel píše do stejného pole, splývají do
    /// jednoho kroku s poslední hodnotou. Výběr ze seznamu (má ValueText) je vždy samostatný krok.
    /// </summary>
    private void ApplyChange(JourneyEvent e, string label)
    {
        var isSelect = e.ValueText is not null;
        var last = _steps.LastOrDefault();

        if (!isSelect && last is { Type: JourneyEventType.FieldChanged, IsSelect: false } && last.Target == e.Target)
        {
            last.EndOffsetMs = e.OffsetMs;
            last.Description = ChangeText(label, e.Value);
            return;
        }

        _changesByField[label] = _changesByField.GetValueOrDefault(label) + 1;
        var step = AddStep(e, isSelect ? $"Vybral „{label}“: {e.ValueText}" : ChangeText(label, e.Value));
        step.IsSelect = isSelect;
    }

    private static string ChangeText(string label, string? value) =>
        string.IsNullOrEmpty(value) ? $"Vymazal pole „{label}“" : $"Vyplnil „{label}“: „{value}“";

    private void CloseFocusedField(long offsetMs)
    {
        if (_focusedField is { } field)
        {
            _timeInFields[field.Label] = _timeInFields.GetValueOrDefault(field.Label) + Math.Max(0, offsetMs - field.FocusedAtMs);
            _focusedField = null;
        }
    }

    private StepBuilder AddStep(JourneyEvent e, string description)
    {
        var step = new StepBuilder(e.Type, e.Target, e.OffsetMs, description);
        _steps.Add(step);
        return step;
    }

    private JourneySummaryDto Summary() => new(
        _submittedAtMs ?? _lastOffsetMs,
        _clickCount,
        _failedSubmits,
        _changesByField,
        _timeInFields
            .OrderByDescending(f => f.Value)
            .Select(f => new JourneyFieldTimeDto(f.Key, f.Value))
            .ToList());

    private IReadOnlyList<JourneyStepDto> Steps() =>
        _steps.Select(s => new JourneyStepDto(s.OffsetMs, s.EndOffsetMs, s.Type.ToString(), s.Description)).ToList();

    private static JourneyEventDto ToDto(JourneyEvent e) =>
        new(e.EventId, e.Type.ToString(), e.OffsetMs, e.Target, e.Label, e.Value, e.ValueText);

    /// <summary>Rozpracovaný krok časové osy (při psaní se mu posouvá konec a popis).</summary>
    private sealed class StepBuilder(JourneyEventType type, string? target, long offsetMs, string description)
    {
        public JourneyEventType Type { get; } = type;
        public string? Target { get; } = target;
        public long OffsetMs { get; } = offsetMs;
        public long EndOffsetMs { get; set; } = offsetMs;
        public string Description { get; set; } = description;
        public bool IsSelect { get; set; }
    }
}
