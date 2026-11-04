namespace MiniShop.Domain.Reviews;

/// <summary>
/// E-shop krok 14: kopie jedné objednávky v modulu Reviews – jen to, co je potřeba k rozhodnutí „smí hodnotit“:
/// kdo, které produkty, jestli dorazily a jestli je nevrátil. Plní se z událostí Orderingu (event-carried state transfer),
/// na Ordering se Reviews nikdy neptá.
/// </summary>
/// <remarks>
/// Zprávy mimo pořadí: doručení může dorazit dřív než vznik – řádek se založí hned (<see cref="Create"/>) a vznik
/// ho doplní. Doručení i vrácení se jen zapnou, nikdy nevypnou (stejný výsledek v libovolném pořadí).
/// </remarks>
public sealed class PurchasedOrder
{
    private readonly List<PurchasedProduct> _products = new();

    // Konstruktor pro EF Core.
    private PurchasedOrder()
    {
    }

    /// <summary>Číslo objednávky z Orderingu (klíč).</summary>
    public int OrderId { get; private set; }

    /// <summary>ID účtu zákazníka; null = vznik objednávky (OrderPlacedV2) ještě nedorazil.</summary>
    public string? CustomerId { get; private set; }

    /// <summary>Jméno z objednávky – autor hodnocení se ukáže jako „Jan N.“.</summary>
    public string? CustomerName { get; private set; }

    public bool IsDelivered { get; private set; }

    public bool IsReturned { get; private set; }

    public IReadOnlyCollection<PurchasedProduct> Products => _products.AsReadOnly();

    /// <summary>Smí zákazník hodnotit tenhle produkt z téhle objednávky? Dostal ho a nevrátil.</summary>
    public bool AllowsReviewOf(string customerId, int productId) =>
        CustomerId == customerId && IsDelivered && !IsReturned && _products.Any(p => p.ProductId == productId);

    public static PurchasedOrder Create(int orderId) => new() { OrderId = orderId };

    /// <summary>Kdo a co objednal (ze vzniku nebo ze snímku). Podruhé přepíše – snímek je čerstvější.</summary>
    public void ApplyPlaced(string customerId, string customerName, IEnumerable<int> productIds)
    {
        CustomerId = customerId;
        CustomerName = customerName;
        _products.Clear();
        _products.AddRange(productIds.Distinct().Select(id => new PurchasedProduct(id)));
    }

    public void MarkDelivered() => IsDelivered = true;

    public void MarkReturned() => IsReturned = true;
}

/// <summary>Produkt z objednávky (jen ID – název ukazuje výloha z kopie katalogu Orderingu).</summary>
public sealed record PurchasedProduct(int ProductId);
