using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders.Carts;

/// <summary>
/// E-shop krok 3: nákupní košík zákazníka – agregát s vlastními pravidly (invarianty): nejvýš <see cref="MaxLines"/>
/// řádků, množství 1–<see cref="MaxQuantity"/>, každý produkt jen jednou (další přidání = navýšení množství).
/// Řádky se mění jen přes metody košíku, takže pravidla nejde obejít.
/// </summary>
/// <remarks>
/// Košík neblokuje zboží – rezervace vzniká až u pokladny (krok 4). Cena v košíku je jen orientační:
/// zobrazuje se vždy aktuální z kopie katalogu, <see cref="CartLine.PriceWhenAdded"/> slouží jen k upozornění na změnu.
/// Košík žije v modulu Ordering (stejná DB jako objednávka) – pokladna tak bude jedna transakce.
/// Doménové události zatím nevyvolává: nikdo by je neposlouchal (připraví je krok 10 – opuštěný košík).
/// </remarks>
public sealed class Cart
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 100;

    private readonly List<CartLine> _lines = new();

    // Konstruktor pro EF Core.
    private Cart()
    {
    }

    /// <param name="customerId">ID uživatelského účtu – pro Ordering neprůhledný řetězec (jako u objednávky).</param>
    public Cart(string customerId, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Košík musí patřit zákazníkovi.");
        }

        CustomerId = customerId;
        UpdatedAtUtc = createdAtUtc;
    }

    public int Id { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>Kdy se košík naposledy změnil (pro krok 10 – opuštěný košík).</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<CartLine> Lines => _lines.AsReadOnly();

    /// <summary>Počet kusů v košíku (číslo v hlavičce webu).</summary>
    public int TotalQuantity => _lines.Sum(l => l.Quantity);

    /// <summary>
    /// Přidá produkt z kopie katalogu. Už v košíku je → navýší množství a zapamatuje si aktuální cenu
    /// (zákazník ji právě viděl ve výloze).
    /// </summary>
    public void AddItem(CatalogProduct product, int quantity, DateTime nowUtc)
    {
        if (product.IsRemoved)
        {
            throw new DomainException($"Produkt '{product.Name}' už není v nabídce.");
        }

        if (product.Availability == CatalogAvailability.OutOfStock)
        {
            throw new DomainException($"Produkt '{product.Name}' je vyprodaný.");
        }

        var line = FindLine(product.Id);
        if (line is not null)
        {
            line.Increase(quantity, product.Price);
        }
        else
        {
            if (_lines.Count >= MaxLines)
            {
                throw new DomainException($"Košík může obsahovat nejvýš {MaxLines} různých produktů.");
            }

            _lines.Add(new CartLine(product.Id, quantity, product.Price));
        }

        UpdatedAtUtc = nowUtc;
    }

    public void ChangeQuantity(int productId, int quantity, DateTime nowUtc)
    {
        var line = FindLine(productId) ?? throw new DomainException("Produkt v košíku není.");
        line.SetQuantity(quantity);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Odebere řádek. Když v košíku není (odebraný v jiné záložce), nic se nestane.</summary>
    public void RemoveItem(int productId, DateTime nowUtc)
    {
        var line = FindLine(productId);
        if (line is null)
        {
            return;
        }

        _lines.Remove(line);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Vyprázdní košík – po objednání (krok 4).</summary>
    public void Clear(DateTime nowUtc)
    {
        _lines.Clear();
        UpdatedAtUtc = nowUtc;
    }

    private CartLine? FindLine(int productId) => _lines.FirstOrDefault(l => l.ProductId == productId);
}
