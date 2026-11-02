using MiniShop.Application.Ordering.Pricing;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 4: pravidla pokladny v doméně – adresa (hodnotový objekt), doprava objednávky, odbavení košíku
/// (verze, změna ceny, nedostupné zboží) a ceník dopravy.
/// </summary>
public class CheckoutDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    // ---------- DeliveryAddress ----------

    [Fact]
    public void Adresa_PscSMezerou_UlozeneBezMezery_AStejneHodnotyJsouStejnaAdresa()
    {
        var address = new DeliveryAddress(" Dlouhá 5 ", "Praha", "110 00");

        Assert.Equal(("Dlouhá 5", "11000", "110 00"), (address.Street, address.PostalCode, address.PostalCodeFormatted));
        Assert.Equal(new DeliveryAddress("Dlouhá 5", "Praha", "11000"), address);   // hodnotový objekt
        Assert.Equal("Dlouhá 5, 110 00 Praha", address.ToString());
    }

    [Theory]
    [InlineData("", "Praha", "11000")]
    [InlineData("Dlouhá 5", " ", "11000")]
    [InlineData("Dlouhá 5", "Praha", "1100")]
    [InlineData("Dlouhá 5", "Praha", "11000a")]
    public void Adresa_Neplatna_Chyba(string street, string city, string postalCode)
    {
        Assert.Throws<DomainException>(() => new DeliveryAddress(street, city, postalCode));
    }

    // ---------- Order.SetShipping ----------

    [Fact]
    public void Doprava_KuryrSAdresou_CenaSePocitaDoCelkove()
    {
        var order = OrderWithItem(price: 500m);

        order.SetShipping(ShippingMethod.Courier, 99m, Address());

        Assert.Equal((500m, 599m), (order.ItemsTotal, order.TotalPrice));
        Assert.NotNull(order.DeliveryAddress);
    }

    [Fact]
    public void Doprava_KuryrBezAdresy_Chyba_OsobniOdberAdresuNeulozi()
    {
        var order = OrderWithItem(price: 500m);

        Assert.Throws<DomainException>(() => order.SetShipping(ShippingMethod.Courier, 99m, null));
        Assert.Throws<DomainException>(() => order.SetShipping(ShippingMethod.None, 0m, null));

        order.SetShipping(ShippingMethod.Pickup, 0m, Address());
        Assert.Null(order.DeliveryAddress);
    }

    [Fact]
    public void Anonymizace_SmazeAdresu()
    {
        var order = OrderWithItem(price: 500m);
        order.SetShipping(ShippingMethod.Courier, 99m, Address());
        order.Confirm(Now);
        order.Cancel(Now);

        order.Anonymize();

        Assert.Null(order.DeliveryAddress);
        Assert.Equal(99m, order.ShippingPrice);   // cena zůstává (účetnictví)
    }

    // ---------- Cart.CheckOut ----------

    [Fact]
    public void CheckOut_VratiRadkyVyprazdniKosikAZmeniVerzi()
    {
        var (cart, catalog) = CartWith(Product(1, 100m), Product(2, 200m));
        var version = cart.CheckoutId;

        var lines = cart.CheckOut(version, catalog, Now);

        Assert.Equal(new[] { (1, 1), (2, 1) }, lines.Select(l => (l.Product.Id, l.Quantity)));
        Assert.Empty(cart.Lines);
        Assert.NotEqual(version, cart.CheckoutId);
    }

    [Fact]
    public void KazdaZmenaKosiku_NovaVerze_StaryFormularPokladnyNeprojde()
    {
        var (cart, catalog) = CartWith(Product(1, 100m));
        var seenAtCheckout = cart.CheckoutId;

        cart.ChangeQuantity(1, 3, Now);   // jiná záložka

        Assert.NotEqual(seenAtCheckout, cart.CheckoutId);
        var ex = Assert.Throws<DomainException>(() => cart.CheckOut(seenAtCheckout, catalog, Now));
        Assert.Contains("změnil", ex.Message);
        Assert.Single(cart.Lines);
    }

    [Fact]
    public void CheckOut_ZmenenaCena_ChybaDokudZakaznikNepotvrdi()
    {
        var mouse = Product(1, 100m);
        var (cart, catalog) = CartWith(mouse);
        mouse.Apply("Myš", 120m, Now.AddMinutes(1));   // zdražení ve skladu

        Assert.Throws<DomainException>(() => cart.CheckOut(cart.CheckoutId, catalog, Now));

        cart.AcceptCurrentPrices(catalog, Now);
        Assert.Equal(120m, Assert.Single(cart.CheckOut(cart.CheckoutId, catalog, Now)).Product.Price);
    }

    [Fact]
    public void CheckOut_VyprodaneNeboStazene_Chyba()
    {
        var soldOut = Product(1, 100m);
        var removed = Product(2, 100m);
        var (cart, catalog) = CartWith(soldOut, removed);
        soldOut.ApplyAvailability(CatalogAvailability.OutOfStock, Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => cart.CheckOut(cart.CheckoutId, catalog, Now));
        cart.RemoveItem(1, Now);
        removed.MarkRemoved(Now.AddMinutes(1));
        Assert.Throws<DomainException>(() => cart.CheckOut(cart.CheckoutId, catalog, Now));
    }

    [Fact]
    public void CheckOut_PrazdnyKosik_Chyba()
    {
        var cart = new Cart("zakaznik-jan", Now);

        Assert.Throws<DomainException>(() => cart.CheckOut(cart.CheckoutId, new Dictionary<int, CatalogProduct>(), Now));
    }

    // ---------- Ceník dopravy ----------

    [Theory]
    [InlineData(ShippingMethod.Pickup, 100, 0)]
    [InlineData(ShippingMethod.Courier, 999.99, 99)]
    [InlineData(ShippingMethod.Courier, 1000, 0)]
    public void CenikDopravy(ShippingMethod method, decimal itemsTotal, decimal expected)
    {
        var calculator = new StandardShippingPriceCalculator(courierPrice: 99m, freeFrom: 1_000m);

        Assert.Equal(expected, calculator.Calculate(method, itemsTotal));
    }

    private static DeliveryAddress Address() => new("Dlouhá 5", "Praha", "11000");

    private static Order OrderWithItem(decimal price)
    {
        var order = new Order("zakaznik-jan", "Jan Novák", "jan@example.com", Now);
        order.AddItem(Product(1, price), 1, 0m);
        return order;
    }

    private static (Cart, Dictionary<int, CatalogProduct>) CartWith(params CatalogProduct[] products)
    {
        var cart = new Cart("zakaznik-jan", Now);
        foreach (var product in products)
        {
            cart.AddItem(product, 1, Now);
        }

        return (cart, products.ToDictionary(p => p.Id));
    }

    private static CatalogProduct Product(int id, decimal price)
    {
        var product = new CatalogProduct(id, "Myš", price, Now);
        product.ApplyAvailability(CatalogAvailability.InStock, Now);
        return product;
    }
}
