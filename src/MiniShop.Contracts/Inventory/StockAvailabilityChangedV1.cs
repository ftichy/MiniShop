namespace MiniShop.Contracts.Inventory;

/// <summary>
/// E-shop krok 2: produkt přešel do jiného pásma dostupnosti (Skladem / Poslední kusy / Vyprodáno).
/// Sklad ji posílá jen při změně pásma, ne při každém prodaném kusu – přesná zásoba patří skladu.
/// </summary>
/// <remarks><paramref name="Availability"/> je hodnota z <see cref="StockAvailabilityV1"/>.</remarks>
public sealed record StockAvailabilityChangedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int ProductId,
    string Availability) : IIntegrationEvent;

/// <summary>Hodnoty dostupnosti v kontraktech – text, ne enum (příjemce nemusí znát typ odesílatele).</summary>
public static class StockAvailabilityV1
{
    /// <summary>Víc než pár kusů.</summary>
    public const string InStock = "InStock";

    /// <summary>Poslední kusy.</summary>
    public const string LowStock = "LowStock";

    /// <summary>Nic na skladě.</summary>
    public const string OutOfStock = "OutOfStock";
}
