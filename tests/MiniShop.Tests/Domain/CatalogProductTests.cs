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

    // ---------- E-shop krok 2: V2 (popis, kategorie), dostupnost, text pro hledání ----------

    [Fact]
    public void ApplyDetails_DoplniPopisKategoriiATextProHledani()
    {
        var product = new CatalogProduct(1, "Myš", 500m, Now);

        Assert.True(product.ApplyDetails("Bezdrátová myš", "2,4 GHz", 890m, 2, "Periferie", null, Now.AddMinutes(1)));

        Assert.Equal(("2,4 GHz", 2, "Periferie"), (product.Description, product.CategoryId, product.CategoryName));
        Assert.Equal("bezdratova mys 2,4 ghz periferie", product.SearchText);
    }

    /// <summary>Sklad posílá stejnou změnu jako V1 i V2 se stejným časem – pořadí doručení nesmí nic ztratit.</summary>
    [Fact]
    public void V1PoV2SeStejnymCasem_PopisZustane()
    {
        var product = new CatalogProduct(1, "Myš", 500m, Now);
        product.ApplyDetails("Myš", "Bezdrátová", 500m, 2, "Periferie", null, Now.AddMinutes(1));

        Assert.True(product.Apply("Myš", 500m, Now.AddMinutes(1)));

        Assert.Equal("Bezdrátová", product.Description);
        Assert.Contains("bezdratova", product.SearchText);
    }

    [Fact]
    public void ApplyDetails_StarsiZmena_SeIgnoruje()
    {
        var product = new CatalogProduct(1, "Myš", 500m, Now.AddMinutes(2));

        Assert.False(product.ApplyDetails("Myš", "Stará", 400m, 2, "Periferie", null, Now.AddMinutes(1)));

        Assert.Null(product.Description);
    }

    [Fact]
    public void ApplyAvailability_StarsiZmena_SeIgnoruje()
    {
        var product = new CatalogProduct(1, "Myš", 500m, Now);
        Assert.Equal(CatalogAvailability.Unknown, product.Availability);

        Assert.True(product.ApplyAvailability(CatalogAvailability.OutOfStock, Now.AddMinutes(2)));
        Assert.False(product.ApplyAvailability(CatalogAvailability.InStock, Now.AddMinutes(1)));   // pozdní zpráva

        Assert.Equal(CatalogAvailability.OutOfStock, product.Availability);
        Assert.False(product.IsOrderable);
    }

    [Theory]
    [InlineData("Myš", "mys")]
    [InlineData("  MYŠ  Pro ", "mys pro")]
    [InlineData("Sluchátka s mikrofonem", "sluchatka s mikrofonem")]
    [InlineData("Žluťoučký kůň", "zlutoucky kun")]
    [InlineData(null, "")]
    public void TextProHledani_MalaPismenaBezDiakritiky(string? text, string expected)
    {
        Assert.Equal(expected, CatalogSearchText.Normalize(text));
    }
}
