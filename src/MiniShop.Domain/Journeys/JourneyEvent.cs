using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Journeys;

/// <summary>
/// Jedna akce uživatele při zakládání objednávky (krok 12, modul Journeys). Neměnný záznam: jen přibývá, nikdy se
/// nemění ani nemaže. Všechny události jedné návštěvy formuláře mají stejné <see cref="JourneyId"/> = proud (event stream).
/// </summary>
/// <remarks>
/// <b>Event sourcing v malém.</b> Neukládáme „výsledný stav formuláře“, ale celý seznam toho, co se dělo.
/// Výsledek (časová osa, souhrn, přehrání) si z proudu kdykoli spočítá projekce – i takový, na který jsme při ukládání nemysleli.
/// Čas je <see cref="OffsetMs"/> od otevření formuláře (hodiny prohlížeče): pořadí a pauzy jsou přesné,
/// i když dávky dorazí na server pozdě nebo v jiném pořadí.
/// </remarks>
public sealed class JourneyEvent
{
    /// <summary>Delší texty se zkrátí – proud nejde zahltit (např. vložením obrovského textu do pole).</summary>
    public const int MaxTextLength = 500;

    // Konstruktor pro EF Core.
    private JourneyEvent()
    {
    }

    public JourneyEvent(
        Guid eventId,
        Guid journeyId,
        JourneyEventType type,
        long offsetMs,
        string? target,
        string? label,
        string? value,
        string? valueText,
        DateTime recordedAtUtc)
    {
        if (eventId == Guid.Empty || journeyId == Guid.Empty)
        {
            throw new DomainException("Událost musí mít EventId i JourneyId.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Neznámý typ události {type}.");
        }

        if (offsetMs < 0)
        {
            throw new DomainException("Čas události od otevření formuláře nesmí být záporný.");
        }

        EventId = eventId;
        JourneyId = journeyId;
        Type = type;
        OffsetMs = offsetMs;
        Target = Truncate(target);
        Label = Truncate(label);
        Value = Truncate(value);
        ValueText = Truncate(valueText);
        RecordedAtUtc = recordedAtUtc;
    }

    /// <summary>Server formulář odmítl – <paramref name="errors"/> jsou hlášky, které uživatel viděl.</summary>
    public static JourneyEvent ValidationFailed(Guid journeyId, long offsetMs, string errors, DateTime recordedAtUtc) =>
        new(Guid.NewGuid(), journeyId, JourneyEventType.ValidationFailed, offsetMs, null, null, errors, null, recordedAtUtc);

    /// <summary>Objednávka vznikla – spojí proud s číslem objednávky.</summary>
    public static JourneyEvent OrderSubmitted(Guid journeyId, long offsetMs, int orderId, DateTime recordedAtUtc) =>
        new(Guid.NewGuid(), journeyId, JourneyEventType.OrderSubmitted, offsetMs, null, null, null, null, recordedAtUtc)
        {
            OrderId = orderId
        };

    /// <summary>Pořadí uložení v databázi (jen pro stabilní řazení událostí se stejným časem).</summary>
    public long Id { get; private set; }

    /// <summary>ID události, které vygeneroval prohlížeč – podle něj server pozná opakovaně poslanou dávku.</summary>
    public Guid EventId { get; private set; }

    /// <summary>Jedna návštěva formuláře (proud událostí). Přežije i neúspěšné odeslání – formulář ho pošle zpět.</summary>
    public Guid JourneyId { get; private set; }

    public JourneyEventType Type { get; private set; }

    /// <summary>Milisekundy od otevření formuláře.</summary>
    public long OffsetMs { get; private set; }

    /// <summary>Technické jméno prvku (Input.CustomerName, nav-orders…) – podle něj přehrávání najde pole.</summary>
    public string? Target { get; private set; }

    /// <summary>Lidský popis prvku (popisek pole, text tlačítka nebo odkazu).</summary>
    public string? Label { get; private set; }

    /// <summary>Hodnota pole (skutečně napsaný text, ID produktu, množství) nebo chybové hlášky.</summary>
    public string? Value { get; private set; }

    /// <summary>Čitelná podoba hodnoty – u výběru produktu jeho název a cena.</summary>
    public string? ValueText { get; private set; }

    /// <summary>Jen u <see cref="JourneyEventType.OrderSubmitted"/>: číslo vzniklé objednávky.</summary>
    public int? OrderId { get; private set; }

    /// <summary>Kdy událost přišla na server (čas serveru, ne prohlížeče).</summary>
    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>Události, které smí zapsat jen server (prohlížeč by mohl podvrhnout cizí číslo objednávky).</summary>
    public static bool IsServerOnly(JourneyEventType type) =>
        type is JourneyEventType.ValidationFailed or JourneyEventType.OrderSubmitted;

    private static string? Truncate(string? text) =>
        text is { Length: > MaxTextLength } ? text[..MaxTextLength] : text;
}
