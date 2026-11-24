using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory.Events;

namespace MiniShop.Domain.Inventory;

/// <summary>
/// Produkt v nabídce obchodu. Stav se mění jen přes metody, které hlídají business pravidla
/// (zapouzdření – settery jsou privátní).
/// </summary>
/// <remarks>
/// Od kroku 10 patří modulu Inventory (vlastní databáze inventory.db). Ordering ho nevidí – drží si vlastní kopii
/// (CatalogProduct), kterou plní události <see cref="ProductChanged"/> / <see cref="ProductRemoved"/>.
/// E-shop krok 2: změna zásoby vyvolá <see cref="StockAvailabilityChanged"/>, ale jen když se změní pásmo dostupnosti
/// (Skladem / Poslední kusy / Vyprodáno) – přesný počet kusů Ordering nepotřebuje.
/// </remarks>
public class Product : Entity
{
    // Konstruktor pro EF Core.
    private Product()
    {
    }

    public Product(string name, string? description, decimal price, Category category, int stockQuantity, DateTime createdAtUtc)
    {
        if (stockQuantity < 0)
        {
            throw new DomainException("Skladová zásoba nemůže být záporná.");
        }

        // Zásoba dřív než Update – ProductChanged (zpráva s dostupností) se sestaví až při uložení, ale ať je stav úplný.
        // E-shop krok 28A: nový produkt má zásobu ve výchozím skladu (Praha).
        StockQuantity = stockQuantity;
        if (stockQuantity > 0)
        {
            _stocks.Add(new WarehouseStock(Warehouse.DefaultId, stockQuantity));
        }

        AvailabilityChangedAtUtc = createdAtUtc;
        Update(name, description, price, category, createdAtUtc);
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    /// <summary>Celkem kusů ve všech skladech (e-shop krok 28A = součet <see cref="Stocks"/>) – z něj se počítá dostupnost ve výloze.</summary>
    public int StockQuantity { get; private set; }

    /// <summary>
    /// Concurrency token zásoby (review #185): zvyšuje se při každé změně zásoby. Zásobu mění souběžně API (naskladnění)
    /// a handlery zpráv (rezervace, vrácení) – bez tokenu by pozdější uložení potichu přepsalo dřívější („lost update“).
    /// </summary>
    public int Version { get; private set; }

    private readonly List<WarehouseStock> _stocks = [];

    /// <summary>E-shop krok 28A: zásoba po skladech (sklad bez kusů tu nemusí být).</summary>
    public IReadOnlyCollection<WarehouseStock> Stocks => _stocks.AsReadOnly();

    /// <summary>
    /// Kolik kusů je v daném skladu. Výchozí sklad (Praha) dostane i rozdíl mezi <see cref="StockQuantity"/> a součtem
    /// skladů – vznikne, když při postupném nasazení chvíli běží starší verze skladu, která zná jen celkovou zásobu.
    /// </summary>
    public int StockIn(int warehouseId) =>
        (_stocks.FirstOrDefault(s => s.WarehouseId == warehouseId)?.Quantity ?? 0)
        + (warehouseId == Warehouse.DefaultId ? LegacyDifference : 0);

    private int LegacyDifference => StockQuantity - _stocks.Sum(s => s.Quantity);

    /// <summary>E-shop krok 2: každý produkt patří do jedné kategorie.</summary>
    public int CategoryId { get; private set; }

    public Category Category { get; private set; } = null!;

    /// <summary>
    /// Kdy se naposledy změnil název, popis, cena nebo kategorie. Při obnově katalogu (<c>SyncCatalogV1</c>) jde ve zprávě
    /// jako čas změny – znovu poslaný stav tak nepřebije novější změnu, která je k příjemci zrovna na cestě.
    /// </summary>
    public DateTime ChangedAtUtc { get; private set; }

    /// <summary>E-shop krok 2: pásmo dostupnosti – počítá se ze zásoby, neukládá se.</summary>
    public StockAvailability Availability => StockAvailabilityBands.From(StockQuantity);

    /// <summary>Kdy se naposledy změnilo pásmo dostupnosti (stejný účel jako <see cref="ChangedAtUtc"/>, jen pro dostupnost).</summary>
    public DateTime AvailabilityChangedAtUtc { get; private set; }

    public void Update(string name, string? description, decimal price, Category category, DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Název produktu je povinný.");
        }

        if (price <= 0)
        {
            throw new DomainException("Cena produktu musí být větší než nula.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = price;
        Category = category ?? throw new DomainException("Produkt musí patřit do kategorie.");
        CategoryId = category.Id;
        MarkChanged(updatedAtUtc);
    }

    /// <summary>
    /// E-shop krok 2B: jméno souboru obrázku v úložišti skladu (null = bez obrázku). Soubor sám v doméně není –
    /// jméno generuje aplikační vrstva z otisku obsahu, takže nový obrázek = nové jméno.
    /// </summary>
    public string? ImageFileName { get; private set; }

    /// <summary>Nový obrázek – pro výlohu je to změna produktu (ProductUpdatedV2 ponese nové jméno souboru).</summary>
    public void SetImage(string fileName, DateTime changedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainException("Jméno souboru obrázku je povinné.");
        }

        ImageFileName = fileName;
        MarkChanged(changedAtUtc);
    }

    public void RemoveImage(DateTime changedAtUtc)
    {
        if (ImageFileName is null)
        {
            return;   // není co odebírat – žádná zpráva
        }

        ImageFileName = null;
        MarkChanged(changedAtUtc);
    }

    /// <summary>
    /// E-shop krok 2: kategorie produktu se přejmenovala. Jméno kategorie jde ve zprávě o produktu (ProductUpdatedV2),
    /// takže z pohledu výlohy se změnil produkt – kopie katalogu v Orderingu dostane nové jméno.
    /// </summary>
    public void CategoryRenamed(DateTime renamedAtUtc) => MarkChanged(renamedAtUtc);

    /// <summary>Produkt se maže z nabídky – před odebráním z repozitáře, aby se dala vyvolat událost.</summary>
    public void MarkRemoved(DateTime removedAtUtc)
    {
        RemoveDomainEvents<ProductChanged>();
        RemoveDomainEvents<StockAvailabilityChanged>();
        Raise(new ProductRemoved(Id, removedAtUtc));
    }

    /// <summary>Naskladnit do skladu (e-shop krok 28A; vrácené zboží jde do skladu, odkud odešlo).</summary>
    public void AddStock(int warehouseId, int quantity, DateTime changedAtUtc)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Množství pro naskladnění musí být větší než nula.");
        }

        SetStockIn(warehouseId, StockIn(warehouseId) + quantity);
        ChangeStock(StockQuantity + quantity, changedAtUtc);
    }

    /// <summary>Vyskladnit ze skladu (rezervace) – jen tolik, kolik v tom skladu je.</summary>
    public void RemoveStock(int warehouseId, int quantity, DateTime changedAtUtc)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Množství pro vyskladnění musí být větší než nula.");
        }

        var inWarehouse = StockIn(warehouseId);
        if (quantity > inWarehouse)
        {
            throw new InsufficientStockException(Name, quantity, inWarehouse);
        }

        SetStockIn(warehouseId, inWarehouse - quantity);
        ChangeStock(StockQuantity - quantity, changedAtUtc);
    }

    /// <summary>Nový počet kusů ve skladu; rozdíl ze starší verze (<see cref="LegacyDifference"/>) se tím zapíše výchozímu skladu.</summary>
    private void SetStockIn(int warehouseId, int quantity)
    {
        if (warehouseId <= 0)
        {
            throw new DomainException("Neznámý sklad.");
        }

        var difference = LegacyDifference;
        if (difference != 0)
        {
            var main = _stocks.FirstOrDefault(s => s.WarehouseId == Warehouse.DefaultId);
            if (main is null)
            {
                _stocks.Add(main = new WarehouseStock(Warehouse.DefaultId, 0));
            }

            main.Quantity = Math.Max(0, main.Quantity + difference);
        }

        var stock = _stocks.FirstOrDefault(s => s.WarehouseId == warehouseId);
        if (stock is null)
        {
            _stocks.Add(new WarehouseStock(warehouseId, quantity));
        }
        else
        {
            stock.Quantity = quantity;
        }
    }

    private void MarkChanged(DateTime changedAtUtc)
    {
        ChangedAtUtc = changedAtUtc;

        // Nový i změněný produkt: Ordering si podle toho aktualizuje svou kopii katalogu (krok 10).
        // Při opakované změně před uložením stačí poslední stav – starší událost zahodíme.
        RemoveDomainEvents<ProductChanged>();
        Raise(new ProductChanged(this, changedAtUtc));
    }

    private void ChangeStock(int newQuantity, DateTime changedAtUtc)
    {
        var before = Availability;
        StockQuantity = newQuantity;
        Version++;
        if (Availability == before)
        {
            return;   // pořád stejné pásmo – výlohu to nezajímá, žádná zpráva
        }

        AvailabilityChangedAtUtc = changedAtUtc;
        RemoveDomainEvents<StockAvailabilityChanged>();
        Raise(new StockAvailabilityChanged(this, Availability, changedAtUtc));
    }
}
