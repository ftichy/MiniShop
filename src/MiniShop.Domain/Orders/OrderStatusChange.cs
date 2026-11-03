namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 5: jeden řádek historie stavů objednávky („sledování zásilky“) – do jakého stavu objednávka přešla,
/// kdy a proč. Vzniká jen uvnitř <see cref="Order"/> při změně stavu, takže stav a historie se uloží v jedné transakci
/// a nikdy si neodporují.
/// </summary>
public class OrderStatusChange
{
    // Konstruktor pro EF Core.
    private OrderStatusChange()
    {
    }

    internal OrderStatusChange(OrderStatus status, DateTime changedAtUtc, string? note)
    {
        Status = status;
        ChangedAtUtc = changedAtUtc;
        Note = note;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// Čas přechodu. Null jen u objednávek starších než e-shop krok 5 – migrace doplnila jejich aktuální stav,
    /// ale kdy do něj přešly, se už nezjistí.
    /// </summary>
    public DateTime? ChangedAtUtc { get; private set; }

    /// <summary>Volitelné vysvětlení (důvod zamítnutí). Bez osobních údajů – anonymizace ho nemaže.</summary>
    public string? Note { get; private set; }
}
