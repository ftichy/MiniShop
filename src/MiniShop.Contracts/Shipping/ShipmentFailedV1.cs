namespace MiniShop.Contracts.Shipping;

/// <summary>
/// E-shop krok 26B: dopravce zásilku odmítl (trvalá chyba – např. neplatné PSČ). Opakování nepomůže, rozhodne člověk
/// v Expedici (zkusit znovu, nebo odeslat jiným dopravcem). Dočasné chyby (dopravce neodpovídá) se sem nedostanou – opakují se.
/// </summary>
/// <param name="Reason">Česká hláška pro skladníka (bez osobních údajů).</param>
public sealed record ShipmentFailedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string Reason) : IIntegrationEvent;
