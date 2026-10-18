namespace MiniShop.Domain.Journeys;

/// <summary>Co uživatel na stránce objednávek udělal (krok 12). Ukládá se jako text – nové typy jen přidávat na konec.</summary>
public enum JourneyEventType
{
    /// <summary>Stránka s formulářem se načetla.</summary>
    FormOpened,

    /// <summary>Kurzor vstoupil do pole formuláře.</summary>
    FieldFocused,

    /// <summary>Hodnota pole se změnila (každý napsaný znak, výběr produktu, změna množství). Value = celá aktuální hodnota.</summary>
    FieldChanged,

    /// <summary>Kurzor opustil pole.</summary>
    FieldBlurred,

    /// <summary>Klik kamkoli na stránce (tlačítko, odkaz v menu…). Target/Label popisují, na co.</summary>
    Clicked,

    /// <summary>Uživatel odeslal formulář (tlačítko Objednat nebo Enter).</summary>
    SubmitClicked,

    /// <summary>Server formulář odmítl (chybějící jméno, neplatný e-mail…). Value = chybové hlášky. Zapisuje jen server.</summary>
    ValidationFailed,

    /// <summary>Objednávka vznikla – tím je proud spojený s jejím číslem (OrderId). Zapisuje jen server.</summary>
    OrderSubmitted
}
