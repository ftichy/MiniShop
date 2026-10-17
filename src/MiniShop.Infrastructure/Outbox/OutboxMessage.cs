namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Řádek v tabulce OutboxMessages: záznam o doménové události uložený ve STEJNÉ transakci jako změna dat.
/// Databáze tak zaručí, že se uloží buď změna i událost, nebo nic – událost se nemůže ztratit.
/// </summary>
/// <remarks>
/// ProcessedAtUtc = null znamená "ještě nedoručeno" (nebo doručení selhalo, viz Error).
/// Takové řádky bude v kroku 5 opakovaně zpracovávat worker na pozadí.
/// </remarks>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Název typu události, např. "OrderCancelled".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Data události jako JSON – jen jednoduché hodnoty, ne celá entita.</summary>
    public string Payload { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; }

    /// <summary>Kdy všechny handlery události úspěšně proběhly; null = zatím ne.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Text poslední chyby při doručení; null = bez chyby.</summary>
    public string? Error { get; set; }
}
