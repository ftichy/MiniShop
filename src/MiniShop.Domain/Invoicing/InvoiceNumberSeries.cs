namespace MiniShop.Domain.Invoicing;

/// <summary>
/// E-shop krok 15: číselná řada faktur pro jeden rok – „2026000001“, „2026000002“… bez děr (zákon chce nepřerušenou řadu).
/// </summary>
/// <remarks>
/// Proč ne sekvence databáze: sekvence číslo nevrací, když transakce spadne (rollback) – v řadě by zůstala díra.
/// Tady se číslo zvýší ve stejné transakci jako vznik faktury: když faktura nevznikne, číslo se nespotřebuje.
/// Souběh (dvě kopie Webu vystavují naráz) hlídá <see cref="Version"/> – druhá transakce skončí konfliktem a zkusí to znovu.
/// </remarks>
public sealed class InvoiceNumberSeries
{
    // Konstruktor pro EF Core.
    private InvoiceNumberSeries()
    {
    }

    public int Year { get; private set; }

    /// <summary>Poslední přidělené pořadí v roce (0 = zatím žádná faktura).</summary>
    public int LastSequence { get; private set; }

    /// <summary>Concurrency token – změní se s každým přiděleným číslem.</summary>
    public int Version { get; private set; }

    public static InvoiceNumberSeries Start(int year) => new() { Year = year };

    /// <summary>Další číslo řady – rok + pořadí na 6 míst.</summary>
    public string Next()
    {
        LastSequence++;
        Version++;
        return $"{Year}{LastSequence:D6}";
    }
}
