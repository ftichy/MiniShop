namespace MiniShop.Domain.Invoicing;

/// <summary>
/// E-shop krok 15B: číselná řada dobropisů pro jeden rok – „D2026000001“… Vlastní řada vedle faktur, bez děr –
/// stejný princip jako <see cref="InvoiceNumberSeries"/> (číslo se zvýší ve stejné transakci, souběh hlídá <see cref="Version"/>).
/// </summary>
/// <remarks>
/// Proč vlastní tabulka, ne sloupec „druh“ v řadě faktur: změna klíče existující tabulky by během postupného nasazení
/// rozbila starší verzi kódu, která ještě čte řadu jen podle roku (expand → contract). Nová tabulka je bezpečná.
/// </remarks>
public sealed class CreditNoteNumberSeries
{
    public const string Prefix = "D";

    // Konstruktor pro EF Core.
    private CreditNoteNumberSeries()
    {
    }

    public int Year { get; private set; }

    /// <summary>Poslední přidělené pořadí v roce (0 = zatím žádný dobropis).</summary>
    public int LastSequence { get; private set; }

    /// <summary>Concurrency token – změní se s každým přiděleným číslem.</summary>
    public int Version { get; private set; }

    public static CreditNoteNumberSeries Start(int year) => new() { Year = year };

    /// <summary>Další číslo řady – „D“ + rok + pořadí na 6 míst.</summary>
    public string Next()
    {
        LastSequence++;
        Version++;
        return $"{Prefix}{Year}{LastSequence:D6}";
    }
}
