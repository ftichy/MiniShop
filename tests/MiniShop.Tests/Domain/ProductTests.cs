using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory;
using MiniShop.Domain.Inventory.Events;

namespace MiniShop.Tests.Domain;

public class ProductTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Category Peripherals = new("Periferie");

    private static Product Mouse(int stock = 10) => new("Myš", null, 500m, Peripherals, stock, Now);

    [Fact]
    public void RemoveStock_SniziZasobu()
    {
        var product = Mouse();

        product.RemoveStock(3, Now);

        Assert.Equal(7, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_VicNezJeSkladem_VyhodiVyjimku()
    {
        var product = Mouse(stock: 2);

        var ex = Assert.Throws<InsufficientStockException>(() => product.RemoveStock(3, Now));

        Assert.Equal(2, ex.Available);
        Assert.Equal(2, product.StockQuantity);
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("Myš", 0)]
    [InlineData("Myš", -5)]
    public void Konstruktor_NeplatnaData_VyhodiVyjimku(string name, int price)
    {
        Assert.Throws<DomainException>(() => new Product(name, null, price, Peripherals, 1, Now));
    }

    [Fact]
    public void Konstruktor_BezKategorie_VyhodiVyjimku()
    {
        Assert.Throws<DomainException>(() => new Product("Myš", null, 500m, null!, 1, Now));
    }

    // ---------- Krok 10: události pro kopii katalogu v Orderingu ----------

    [Fact]
    public void NovyProdukt_VyvolaProductChanged()
    {
        var product = Mouse();

        var changed = Assert.IsType<ProductChanged>(Assert.Single(product.DomainEvents));
        Assert.Same(product, changed.Product);
        Assert.Equal(Now, changed.OccurredAtUtc);
    }

    /// <summary>Čas poslední změny posílá sklad při obnově katalogu (SyncCatalogV1).</summary>
    [Fact]
    public void Zmena_SiZapamatujeCasPosledniZmeny()
    {
        var product = Mouse();
        Assert.Equal(Now, product.ChangedAtUtc);

        product.Update("Myš Pro", null, 600m, Peripherals, Now.AddMinutes(1));
        product.AddStock(5, Now.AddMinutes(2));   // změna zásoby není změna katalogu

        Assert.Equal(Now.AddMinutes(1), product.ChangedAtUtc);
    }

    [Fact]
    public void VicZmenPredUlozenim_ZustanePosledniUdalost()
    {
        var product = Mouse();

        product.Update("Myš Pro", null, 600m, Peripherals, Now.AddMinutes(1));

        var changed = Assert.IsType<ProductChanged>(Assert.Single(product.DomainEvents));
        Assert.Equal(Now.AddMinutes(1), changed.OccurredAtUtc);
    }

    [Fact]
    public void MarkRemoved_VyvolaProductRemoved()
    {
        var product = Mouse();
        product.ClearDomainEvents();

        product.MarkRemoved(Now);

        Assert.IsType<ProductRemoved>(Assert.Single(product.DomainEvents));
    }

    // ---------- E-shop krok 2: kategorie a pásma dostupnosti ----------

    [Fact]
    public void Update_ZmeniKategorii()
    {
        var product = Mouse();
        var audio = new Category("Audio");

        product.Update("Myš", null, 500m, audio, Now.AddMinutes(1));

        Assert.Same(audio, product.Category);
    }

    [Fact]
    public void PrejmenovaniKategorie_VyvolaProductChanged()
    {
        var product = Mouse();
        product.ClearDomainEvents();

        product.CategoryRenamed(Now.AddMinutes(1));

        // Jméno kategorie jde ve zprávě o produktu – výloha se o přejmenování dozví od produktu.
        var changed = Assert.IsType<ProductChanged>(Assert.Single(product.DomainEvents));
        Assert.Equal(Now.AddMinutes(1), product.ChangedAtUtc);
        Assert.Equal(Now.AddMinutes(1), changed.OccurredAtUtc);
    }

    [Theory]
    [InlineData(0, StockAvailability.OutOfStock)]
    [InlineData(1, StockAvailability.LowStock)]
    [InlineData(5, StockAvailability.LowStock)]
    [InlineData(6, StockAvailability.InStock)]
    public void Dostupnost_PodlePasem(int stock, StockAvailability expected)
    {
        Assert.Equal(expected, Mouse(stock).Availability);
    }

    [Fact]
    public void ZmenaZasobyVeStejnemPasmu_NevyvolaUdalost()
    {
        var product = Mouse(stock: 20);
        product.ClearDomainEvents();

        product.AddStock(5, Now);
        product.RemoveStock(10, Now);   // 15 ks – pořád „Skladem“

        Assert.Empty(product.DomainEvents);   // přesný počet kusů výlohu nezajímá
    }

    [Fact]
    public void ZmenaPasma_VyvolaStockAvailabilityChanged()
    {
        var product = Mouse(stock: 7);
        product.ClearDomainEvents();

        product.RemoveStock(3, Now.AddMinutes(1));   // 4 ks → „Poslední kusy“

        var changed = Assert.IsType<StockAvailabilityChanged>(Assert.Single(product.DomainEvents));
        Assert.Equal(StockAvailability.LowStock, changed.Availability);
        Assert.Equal(Now.AddMinutes(1), product.AvailabilityChangedAtUtc);
    }

    [Fact]
    public void VicZmenPasmaPredUlozenim_ZustanePosledniUdalost()
    {
        var product = Mouse(stock: 6);
        product.ClearDomainEvents();

        product.RemoveStock(2, Now);                  // 4 → LowStock
        product.RemoveStock(4, Now.AddMinutes(1));    // 0 → OutOfStock

        var changed = Assert.IsType<StockAvailabilityChanged>(Assert.Single(product.DomainEvents));
        Assert.Equal(StockAvailability.OutOfStock, changed.Availability);
    }

    [Fact]
    public void MarkRemoved_ZahodiNeulozenouZmenuDostupnosti()
    {
        var product = Mouse(stock: 6);
        product.ClearDomainEvents();
        product.RemoveStock(6, Now);

        product.MarkRemoved(Now);

        Assert.IsType<ProductRemoved>(Assert.Single(product.DomainEvents));
    }
}
