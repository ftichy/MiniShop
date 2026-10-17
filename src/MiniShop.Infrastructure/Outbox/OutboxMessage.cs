namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Řádek v tabulce OutboxMessages: záznam o doménové události uložený ve STEJNÉ transakci jako změna dat.
/// Databáze tak zaručí, že se uloží buď změna i událost, nebo nic – událost se nemůže ztratit.
/// </summary>
/// <remarks>
/// ProcessedAtUtc = null znamená "ještě nedoručeno" (nebo doručení selhalo, viz Error).
/// Takové řádky zpracovává <see cref="OutboxProcessor"/> (krok 5), dokud neuspěje nebo nevyčerpá pokusy.
/// </remarks>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Doménová (pro handlery v aplikaci), nebo integrační (ven pro jiné služby, krok 7)?</summary>
    public OutboxMessageKind Kind { get; init; }

    /// <summary>Název typu události, např. "OrderCancelled" nebo "OrderCancelledV1".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Data události jako JSON – jen jednoduché hodnoty, ne celá entita.</summary>
    public string Payload { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// Krok 11: "podací číslo" operace, která zprávu vytvořila (W3C traceparent: 00-{trace id}-{span id}-01).
    /// Worker podle něj naváže doručení na původní trasu, i když zpráva v outboxu chvíli ležela. Null = nesledováno.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>Kdy všechny handlery události úspěšně proběhly; null = zatím ne.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Text poslední chyby při doručení; null = bez chyby.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Počet neúspěšných pokusů o doručení. Po <see cref="OutboxProcessor.MaxAttempts"/> se zpráva
    /// přestane zkoušet ("poison message") a čeká, až se na ni podívá člověk.
    /// </summary>
    public int Attempts { get; set; }
}

/// <summary>Druh zprávy v outboxu – podle něj worker rozhodne, kam ji doručit.</summary>
public enum OutboxMessageKind
{
    /// <summary>Doménová událost – worker ji předá dispatcheru a handlerům v této aplikaci.</summary>
    Domain,

    /// <summary>Integrační zpráva – událost nebo příkaz z MiniShop.Contracts – worker ji pošle přes IIntegrationBus.</summary>
    Integration
}
