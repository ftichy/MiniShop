namespace MiniShop.Domain.Reporting;

/// <summary>
/// Proud B2: časy jedné objednávky pro přehled <b>Rychlost vyřízení</b> – kdy vznikla, byla potvrzená, odeslaná a doručená.
/// Plní ji jen přehrávání proudu zpráv (OrderStatusChangedV1, ShipmentStatusChangedV1) – Ordering o ní neví.
/// </summary>
/// <remarks>
/// Každý čas se zapíše jen poprvé – opakovaná nebo pozdní zpráva nic nezmění (přehrání je tak bezpečné i dvakrát).
/// Bez osobních údajů – jen číslo objednávky a časy.
/// </remarks>
public sealed class OrderLeadTime
{
    // Konstruktor pro EF Core.
    private OrderLeadTime()
    {
    }

    public int OrderId { get; private set; }

    public DateTime? PlacedAtUtc { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? ShippedAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    /// <summary>Zásilku vezl dopravce (přišel krok zásilky) – doba doručení se počítá jen u nich (osobní odběr = čekání na zákazníka).</summary>
    public bool ByCarrier { get; private set; }

    public static OrderLeadTime Create(int orderId) => new() { OrderId = orderId };

    /// <summary>Stav objednávky (text z OrderStatusChangedV1) v daném čase – zapíše se jen první výskyt.</summary>
    public void ApplyStatus(string status, DateTime atUtc)
    {
        switch (status)
        {
            case "Pending":
                PlacedAtUtc ??= atUtc;
                break;
            case "Confirmed":
                ConfirmedAtUtc ??= atUtc;
                break;
            case "Shipped":
                ShippedAtUtc ??= atUtc;
                break;
            case "Completed":
                DeliveredAtUtc ??= atUtc;
                break;
        }
    }

    public void MarkByCarrier() => ByCarrier = true;

    /// <summary>Potvrzení: od vzniku po potvrzení ságou (sklad, u karty i zaplacení).</summary>
    public TimeSpan? Confirmation => PlacedAtUtc is { } from && ConfirmedAtUtc is { } to && to >= from ? to - from : null;

    /// <summary>Expedice: od potvrzení po odeslání (předání dopravci / připraveno k vyzvednutí).</summary>
    public TimeSpan? Expedition => ConfirmedAtUtc is { } from && ShippedAtUtc is { } to && to >= from ? to - from : null;

    /// <summary>Doručení: od odeslání po doručení – jen zásilky dopravce.</summary>
    public TimeSpan? Delivery => ByCarrier && ShippedAtUtc is { } from && DeliveredAtUtc is { } to && to >= from ? to - from : null;
}

/// <summary>
/// Proud B2: <b>záložka</b> čtenáře proudu – číslo poslední zpracované zprávy (offset). Ukládá se ve stejné transakci jako
/// data projekce, takže po pádu čtenář pokračuje přesně tam, kde skončil (efekt „přesně jednou“, bez inboxu).
/// </summary>
/// <remarks>
/// Přestavění = <see cref="Reset"/>: offset zpět na začátek a nová <see cref="Generation"/> – běžící čtenář podle ní pozná,
/// že má zahodit rozpracovanou dávku a číst znovu od začátku. <see cref="Version"/> hlídá souběh (dávka vs. přestavění).
/// </remarks>
public sealed class StreamCheckpoint
{
    // Konstruktor pro EF Core.
    private StreamCheckpoint()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Poslední zpracovaná zpráva; null = ještě nic (číst od začátku proudu).</summary>
    public long? Offset { get; private set; }

    public int Generation { get; private set; }

    /// <summary>Čas faktu poslední zpracované zprávy – „data k…“.</summary>
    public DateTime? LastMessageAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public int Version { get; private set; }

    public static StreamCheckpoint Start(string name, DateTime nowUtc) => new() { Name = name, UpdatedAtUtc = nowUtc };

    public void Advance(long offset, DateTime? lastMessageAtUtc, DateTime nowUtc)
    {
        if (Offset is { } current && offset <= current)
        {
            return;   // záložka jen dopředu
        }

        Offset = offset;
        LastMessageAtUtc = lastMessageAtUtc ?? LastMessageAtUtc;
        UpdatedAtUtc = nowUtc;
        Version++;
    }

    public void Reset(DateTime nowUtc)
    {
        Offset = null;
        LastMessageAtUtc = null;
        Generation++;
        UpdatedAtUtc = nowUtc;
        Version++;
    }
}
