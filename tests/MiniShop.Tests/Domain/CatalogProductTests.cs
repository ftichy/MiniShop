using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Domain;

/// <summary>
/// Krok 10: kopie katalogu v Orderingu. Zprávy ze skladu můžou přijít pozdě nebo v jiném pořadí –
/// kopie musí skončit ve stavu podle NEJNOVĚJŠÍ změny.
/// </summary>
public class CatalogProductTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Apply_NovejsiZmena_Prepise()
    {
        var product = new CatalogProduct(1, "Myš", 500m, Now);

        Assert.True(product.Apply("Myš Pro", 600m, Now.AddMinutes(1)));

        Assert.Equal(("Myš Pro", 600m), (product.Name, product.Price));
    }

    [Fact]
    public void Apply_StarsiZmena_SeIgnoruje()
    {
        var product = new CatalogProduct(1, "Myš", 600m, Now.AddMinutes(1));

        Assert.False(product.Apply("Myš", 500m, Now));   // zpráva doručená pozdě (retry)

        Assert.Equal(600m, product.Price);
    }

    [Fact]
    public void MarkRemoved_PozdniStarsiZmenaProduktNevzkrisi()
    {
        var product = new CatalogProduct(1, "Myš", 500m, Now);
        product.MarkRemoved(Now.AddMinutes(2));

        product.Apply("Myš", 450m, Now.AddMinutes(1));

        Assert.True(product.IsRemoved);
    }

    [Fact]
    public void OdebranyProdukt_NejdeObjednat()
    {
        var product = new CatalogProduct(1, "Myš", 500m, Now);
        product.MarkRemoved(Now);
        var order = new Order("zakaznik-1", "Jan Novák", "jan@example.com", Now);

        Assert.Throws<DomainException>(() => order.AddItem(product, 1, 0m));
    }
}
