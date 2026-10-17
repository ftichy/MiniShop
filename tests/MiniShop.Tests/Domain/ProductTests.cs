using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory;
using MiniShop.Domain.Inventory.Events;

namespace MiniShop.Tests.Domain;

public class ProductTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RemoveStock_SniziZasobu()
    {
        var product = new Product("Myš", null, 500m, 10, Now);

        product.RemoveStock(3);

        Assert.Equal(7, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_VicNezJeSkladem_VyhodiVyjimku()
    {
        var product = new Product("Myš", null, 500m, 2, Now);

        var ex = Assert.Throws<InsufficientStockException>(() => product.RemoveStock(3));

        Assert.Equal(2, ex.Available);
        Assert.Equal(2, product.StockQuantity);
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("Myš", 0)]
    [InlineData("Myš", -5)]
    public void Konstruktor_NeplatnaData_VyhodiVyjimku(string name, int price)
    {
        Assert.Throws<DomainException>(() => new Product(name, null, price, 1, Now));
    }

    // ---------- Krok 10: události pro kopii katalogu v Orderingu ----------

    [Fact]
    public void NovyProdukt_VyvolaProductChanged()
    {
        var product = new Product("Myš", null, 500m, 10, Now);

        var changed = Assert.IsType<ProductChanged>(Assert.Single(product.DomainEvents));
        Assert.Same(product, changed.Product);
        Assert.Equal(Now, changed.OccurredAtUtc);
    }

    /// <summary>Čas poslední změny posílá sklad při obnově katalogu (SyncCatalogV1).</summary>
    [Fact]
    public void Zmena_SiZapamatujeCasPosledniZmeny()
    {
        var product = new Product("Myš", null, 500m, 10, Now);
        Assert.Equal(Now, product.ChangedAtUtc);

        product.Update("Myš Pro", null, 600m, Now.AddMinutes(1));
        product.AddStock(5);   // změna zásoby není změna katalogu

        Assert.Equal(Now.AddMinutes(1), product.ChangedAtUtc);
    }

    [Fact]
    public void VicZmenPredUlozenim_ZustanePosledniUdalost()
    {
        var product = new Product("Myš", null, 500m, 10, Now);

        product.Update("Myš Pro", null, 600m, Now.AddMinutes(1));

        var changed = Assert.IsType<ProductChanged>(Assert.Single(product.DomainEvents));
        Assert.Equal(Now.AddMinutes(1), changed.OccurredAtUtc);
    }

    [Fact]
    public void ZmenaZasoby_NevyvolaUdalost()
    {
        var product = new Product("Myš", null, 500m, 10, Now);
        product.ClearDomainEvents();

        product.AddStock(5);
        product.RemoveStock(3);

        Assert.Empty(product.DomainEvents);   // zásoba Ordering nezajímá
    }

    [Fact]
    public void MarkRemoved_VyvolaProductRemoved()
    {
        var product = new Product("Myš", null, 500m, 10, Now);
        product.ClearDomainEvents();

        product.MarkRemoved(Now);

        Assert.IsType<ProductRemoved>(Assert.Single(product.DomainEvents));
    }
}
