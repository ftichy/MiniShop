using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Inventory;

/// <summary>
/// Rezervace zboží pro objednávku (krok 9, modul Inventory). Při rezervaci se odečte StockQuantity produktu,
/// záznam si pamatuje, kolik se má při uvolnění (kompenzaci) vrátit.
/// </summary>
/// <remarks>
/// Inventory zná objednávku jen podle OrderId z příkazu – nesahá na tabulku Orders (příprava na krok 10).
/// </remarks>
public sealed class StockReservation
{
    // Konstruktor pro EF Core.
    private StockReservation()
    {
    }

    public StockReservation(int orderId, int productId, int quantity, DateTime reservedAtUtc)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Rezervované množství musí být větší než nula.");
        }

        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        ReservedAtUtc = reservedAtUtc;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public int ProductId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }

    /// <summary>Kdy byla rezervace uvolněna; null = stále platí.</summary>
    public DateTime? ReleasedAtUtc { get; private set; }

    public bool IsActive => ReleasedAtUtc is null;

    public void Release(DateTime releasedAtUtc)
    {
        if (!IsActive)
        {
            throw new DomainException("Rezervace už byla uvolněna.");
        }

        ReleasedAtUtc = releasedAtUtc;
    }
}
