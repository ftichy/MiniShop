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
    /// Od e-shop kroku 22 = kdy se vrátil <b>poslední</b> kus (rezervace je vrácená celá).
    /// </summary>
    public DateTime? ReturnedAtUtc { get; private set; }

    /// <summary>
    /// E-shop krok 22: kolik kusů z rezervace se už vrátilo na sklad (vratky po částech). Starší celé vratky mají
    /// jen <see cref="ReturnedAtUtc"/> – migrace jim doplnila celé množství.
    /// </summary>
    public int ReturnedQuantity { get; private set; }

    /// <summary>Platí ještě (zboží „pod pultem“) – ani uvolněná, ani vydaná.</summary>
    public bool IsActive => ReleasedAtUtc is null && FulfilledAtUtc is null;

    /// <summary>
    /// E-shop krok 22: kolik kusů ještě jde vrátit – jen u vydaného zboží. Vrácená rezervace (<see cref="ReturnedAtUtc"/>,
    /// i ze starší verze kódu, která množství nepočítá) už nic.
    /// </summary>
    public int ReturnableQuantity => FulfilledAtUtc is null || ReturnedAtUtc is not null ? 0 : Quantity - ReturnedQuantity;

    /// <summary>E-shop krok 12: zboží odešlo a ještě se (celé) nevrátilo – vratka ho smí naskladnit.</summary>
    public bool CanBeReturned => ReturnableQuantity > 0;

    /// <summary>
    /// E-shop krok 12: vydané zboží se vrátilo celé (vratka celé objednávky) – vrátí se všechny zbývající kusy.
    /// Kusy vrací na sklad volající (<c>Product.AddStock</c>) – rezervace jen eviduje, že se to stalo, aby se nenaskladnilo dvakrát.
    /// </summary>
    /// <returns>Kolik kusů se vrátilo (zbytek po předchozích částečných vratkách).</returns>
    public int MarkReturned(DateTime returnedAtUtc)
    {
        EnsureReturnable();
        var quantity = ReturnableQuantity;
        Return(quantity, returnedAtUtc);
        return quantity;
    }

    /// <summary>
    /// E-shop krok 22: vrátila se část kusů (jedna vratka). Poslední kus nastaví <see cref="ReturnedAtUtc"/>.
    /// </summary>
    public void Return(int quantity, DateTime returnedAtUtc)
    {
        EnsureReturnable();
        if (quantity <= 0 || quantity > ReturnableQuantity)
        {
            throw new DomainException($"Vrátit lze 1 až {ReturnableQuantity} ks z této rezervace.");
        }

        ReturnedQuantity += quantity;
        if (ReturnedQuantity == Quantity)
        {
            ReturnedAtUtc = returnedAtUtc;
        }
    }

    private void EnsureReturnable()
    {
        if (!CanBeReturned)
        {
            throw new DomainException(FulfilledAtUtc is null
                ? "Vrátit lze jen vydané zboží."
                : "Zboží z této rezervace už je zpět na skladu.");
        }
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
