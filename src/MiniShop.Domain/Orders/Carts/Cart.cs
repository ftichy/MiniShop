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
        CheckoutId = Guid.NewGuid();
    }

    public int Id { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>Kdy se košík naposledy změnil (pro krok 10 – opuštěný košík).</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<CartLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// E-shop krok 4: „verze“ obsahu košíku pro pokladnu. Mění se při každé změně košíku i po objednání.
    /// Formulář pokladny ji pošle zpět: stejná verze = zákazník objednává přesně to, co viděl; po objednání
    /// stejné ID najde hotovou objednávku (idempotence – dvojklik, F5). Hlídá i souběh (concurrency token).
    /// </summary>
    public Guid CheckoutId { get; private set; }

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

        Changed(nowUtc);
    }

    public void ChangeQuantity(int productId, int quantity, DateTime nowUtc)
    {
        var line = FindLine(productId) ?? throw new DomainException("Produkt v košíku není.");
        line.SetQuantity(quantity);
        Changed(nowUtc);
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
        Changed(nowUtc);
    }

    /// <summary>Vyprázdní košík.</summary>
    public void Clear(DateTime nowUtc)
    {
        _lines.Clear();
        Changed(nowUtc);
    }

    /// <summary>
    /// E-shop krok 4: zákazník viděl nové ceny a souhlasí s nimi – zapamatují se jako ceny „při přidání“,
    /// upozornění zmizí a pokladna je pustí.
    /// </summary>
    public void AcceptCurrentPrices(IReadOnlyDictionary<int, CatalogProduct> catalog, DateTime nowUtc)
    {
        foreach (var line in _lines)
        {
            if (catalog.TryGetValue(line.ProductId, out var product))
            {
                line.AcceptPrice(product.Price);
            }
        }

        Changed(nowUtc);
    }

    /// <summary>
    /// E-shop krok 4: pokladna – ověří, že zákazník objednává přesně to, co viděl, a vyprázdní košík.
    /// Vrací řádky pro objednávku; objednávku zakládá aplikační vrstva ve stejné transakci.
    /// </summary>
    /// <param name="checkoutId">Verze košíku z formuláře pokladny (<see cref="CheckoutId"/>).</param>
    /// <param name="catalog">Produkty košíku z kopie katalogu (i stažené).</param>
    public IReadOnlyList<(CatalogProduct Product, int Quantity)> CheckOut(
        Guid checkoutId, IReadOnlyDictionary<int, CatalogProduct> catalog, DateTime nowUtc)
    {
        if (checkoutId != CheckoutId)
        {
            throw new DomainException("Košík se mezitím změnil. Zkontrolujte ho a objednejte znovu.");
        }

        if (_lines.Count == 0)
        {
            throw new DomainException("Košík je prázdný.");
        }

        var result = new List<(CatalogProduct, int)>();
        foreach (var line in _lines.OrderBy(l => l.Id))
        {
            if (!catalog.TryGetValue(line.ProductId, out var product) || product.IsRemoved)
            {
                throw new DomainException("Košík obsahuje produkt, který už není v nabídce. Odeberte ho z košíku.");
            }

            if (product.Availability == CatalogAvailability.OutOfStock)
            {
                throw new DomainException($"Produkt '{product.Name}' je vyprodaný. Odeberte ho z košíku.");
            }

            // Žádné tiché zdražení (ani zlevnění): změnu ceny musí zákazník vidět a potvrdit.
            if (product.Price != line.PriceWhenAdded)
            {
                throw new DomainException("Ceny některých produktů se změnily. Zkontrolujte je a potvrďte nové ceny.");
            }

            result.Add((product, line.Quantity));
        }

        Clear(nowUtc);
        return result;
    }

    /// <summary>Každá změna obsahu = nová verze pro pokladnu (starý formulář pokladny už neprojde).</summary>
    private void Changed(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        CheckoutId = Guid.NewGuid();
    }

    private CartLine? FindLine(int productId) => _lines.FirstOrDefault(l => l.ProductId == productId);
}
