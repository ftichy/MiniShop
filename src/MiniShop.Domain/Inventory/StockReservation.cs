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

    /// <summary>Kdy byla rezervace uvolněna (zboží zpět na sklad); null = neuvolněná.</summary>
    public DateTime? ReleasedAtUtc { get; private set; }

    /// <summary>
    /// E-shop krok 6: kdy zboží odešlo zákazníkovi – rezervace se změnila na definitivní výdej; null = ještě ne.
    /// Zásoba se už při rezervaci odečetla, výdej ji nemění – jen uzavře evidenci.
    /// </summary>
    public DateTime? FulfilledAtUtc { get; private set; }

    /// <summary>
    /// E-shop krok 12: kdy se vydané zboží vrátilo na sklad (vratka zákazníka); null = nevrácené.
    /// Vrátit jde jen vydanou rezervaci a jen jednou.
    /// </summary>
    public DateTime? ReturnedAtUtc { get; private set; }

    /// <summary>Platí ještě (zboží „pod pultem“) – ani uvolněná, ani vydaná.</summary>
    public bool IsActive => ReleasedAtUtc is null && FulfilledAtUtc is null;

    /// <summary>E-shop krok 12: zboží odešlo a ještě se nevrátilo – vratka ho smí naskladnit.</summary>
    public bool CanBeReturned => FulfilledAtUtc is not null && ReturnedAtUtc is null;

    /// <summary>
    /// E-shop krok 12: vydané zboží se vrátilo (vratka). Kusy vrací na sklad volající (<c>Product.AddStock</c>) –
    /// rezervace jen eviduje, že se to stalo, aby se nenaskladnilo dvakrát.
    /// </summary>
    public void MarkReturned(DateTime returnedAtUtc)
    {
        if (!CanBeReturned)
        {
            throw new DomainException(FulfilledAtUtc is null
                ? "Vrátit lze jen vydané zboží."
                : "Zboží z této rezervace už je zpět na skladu.");
        }

        ReturnedAtUtc = returnedAtUtc;
    }

    public void Release(DateTime releasedAtUtc)
    {
        EnsureActive();
        ReleasedAtUtc = releasedAtUtc;
    }

    /// <summary>E-shop krok 6: zboží odešlo – vydanou rezervaci už nejde uvolnit (nevrátí se na sklad).</summary>
    public void Fulfil(DateTime fulfilledAtUtc)
    {
        EnsureActive();
        FulfilledAtUtc = fulfilledAtUtc;
    }

    private void EnsureActive()
    {
        if (ReleasedAtUtc is not null)
        {
            throw new DomainException("Rezervace už byla uvolněna.");
        }

        if (FulfilledAtUtc is not null)
        {
            throw new DomainException("Rezervace už byla vydaná – zboží odešlo zákazníkovi.");
        }
    }
}
