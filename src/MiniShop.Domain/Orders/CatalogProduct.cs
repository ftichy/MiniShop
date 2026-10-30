namespace MiniShop.Domain.Orders;

/// <summary>
/// Lokální kopie produktu v modulu Ordering (read model, krok 10): to, co objednávka a výloha potřebují.
/// Plní ji události ProductUpdatedV1/V2, StockAvailabilityChangedV1 a ProductRemovedV1 ze skladu.
/// Ordering tak nikdy nečte databázi skladu.
/// </summary>
/// <remarks>
/// Kopie je "eventually consistent": nový produkt se v ní objeví až za pár sekund.
/// Zprávy můžou přijít v jiném pořadí (retry) – proto si pamatuje čas poslední změny a starší změnu ignoruje.
/// Produkt a dostupnost mají každý svůj čas: změna ceny a vyprodání jsou nezávislé zprávy.
/// Odebraný produkt se nemaže, jen označí (IsRemoved): pozdě doručená starší změna by ho jinak vzkřísila.
/// </remarks>
public sealed class CatalogProduct
{
    // Konstruktor pro EF Core.
    private CatalogProduct()
    {
    }

    public CatalogProduct(int id, string name, decimal price, DateTime changedAtUtc)
    {
        Id = id;
        Name = name;
        Price = price;
        ChangedAtUtc = changedAtUtc;
        UpdateSearchText();
    }

    /// <summary>ID produktu ve skladu (není generované tady).</summary>
    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public bool IsRemoved { get; private set; }

    /// <summary>Kdy se stala poslední promítnutá změna (OccurredAtUtc ze zprávy).</summary>
    public DateTime ChangedAtUtc { get; private set; }

    // ---------- E-shop krok 2: výloha (z ProductUpdatedV2 a StockAvailabilityChangedV1) ----------

    public string? Description { get; private set; }

    /// <summary>Kategorie ze skladu – null, dokud nepřišla V2 (produkt z doby před krokem 2).</summary>
    public int? CategoryId { get; private set; }

    public string? CategoryName { get; private set; }

    /// <summary>Jméno souboru obrázku ve skladu (krok 2B) – null = bez obrázku.</summary>
    public string? ImageFileName { get; private set; }

    public CatalogAvailability Availability { get; private set; }

    /// <summary>Kdy se stala poslední promítnutá změna dostupnosti.</summary>
    public DateTime AvailabilityChangedAtUtc { get; private set; }

    /// <summary>Název, popis a kategorie pro hledání – malá písmena bez diakritiky (<see cref="CatalogSearchText"/>).</summary>
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Dá se objednat? Vyprodané se ve výloze ukáže, ale sága by ho zamítla.</summary>
    public bool IsOrderable => !IsRemoved && Availability != CatalogAvailability.OutOfStock;

    /// <summary>ProductUpdatedV1 – jen název a cena.</summary>
    /// <returns>false, když je změna starší než to, co už kopie zná (zpráva přišla pozdě).</returns>
    public bool Apply(string name, decimal price, DateTime changedAtUtc)
    {
        if (changedAtUtc < ChangedAtUtc)
        {
            return false;
        }

        Name = name;
        Price = price;
        ChangedAtUtc = changedAtUtc;
        UpdateSearchText();
        return true;
    }

    /// <summary>
    /// E-shop krok 2: ProductUpdatedV2 – celý stav pro výlohu. Se stejným časem jako V1 téže změny se obě použijí
    /// (nesou stejný název a cenu), V2 navíc doplní popis a kategorii.
    /// </summary>
    /// <returns>false, když je změna starší než to, co už kopie zná.</returns>
    public bool ApplyDetails(string name, string? description, decimal price, int categoryId, string categoryName,
        string? imageFileName, DateTime changedAtUtc)
    {
        if (!Apply(name, price, changedAtUtc))
        {
            return false;
        }

        Description = description;
        CategoryId = categoryId;
        CategoryName = categoryName;
        ImageFileName = imageFileName;
        UpdateSearchText();
        return true;
    }

    /// <summary>E-shop krok 2: StockAvailabilityChangedV1 (nebo dostupnost ve V2).</summary>
    /// <returns>false, když je změna starší než to, co už kopie zná.</returns>
    public bool ApplyAvailability(CatalogAvailability availability, DateTime changedAtUtc)
    {
        if (Availability != CatalogAvailability.Unknown && changedAtUtc < AvailabilityChangedAtUtc)
        {
            return false;
        }

        Availability = availability;
        AvailabilityChangedAtUtc = changedAtUtc;
        return true;
    }

    public bool MarkRemoved(DateTime removedAtUtc)
    {
        if (removedAtUtc < ChangedAtUtc)
        {
            return false;
        }

        IsRemoved = true;
        ChangedAtUtc = removedAtUtc;
        return true;
    }

    private void UpdateSearchText() => SearchText = CatalogSearchText.Normalize($"{Name} {Description} {CategoryName}");
}
