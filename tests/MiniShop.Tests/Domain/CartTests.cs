using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 3: invarianty košíku – nejvýš 20 řádků, množství 1–100, každý produkt jen jednou,
/// nic odebraného ani vyprodaného.
/// </summary>
public class CartTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AddItem_StejnyProduktPodruhe_NavysiMnozstviMistoNovehoRadku()
    {
        var cart = NewCart();
        var mouse = Product(1, price: 500m);

        cart.AddItem(mouse, 2, Now);
        cart.AddItem(mouse, 3, Now.AddMinutes(1));

        var line = Assert.Single(cart.Lines);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(5, cart.TotalQuantity);
        Assert.Equal(Now.AddMinutes(1), cart.UpdatedAtUtc);
    }

    [Fact]
    public void AddItem_PodruheSNovouCenou_ZapamatujeAktualniCenu()
    {
        var cart = NewCart();
        var mouse = Product(1, price: 500m);
        cart.AddItem(mouse, 1, Now);

        mouse.Apply("Myš", 450m, Now.AddMinutes(1));   // zlevnění ze skladu
        cart.AddItem(mouse, 1, Now.AddMinutes(2));

        Assert.Equal(450m, Assert.Single(cart.Lines).PriceWhenAdded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void AddItem_MnozstviMimoRozsah_Chyba(int quantity)
    {
        var cart = NewCart();

        Assert.Throws<DomainException>(() => cart.AddItem(Product(1), quantity, Now));
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public void AddItem_NavyseniNad100_ChybaAMnozstviZustane()
    {
        var cart = NewCart();
        var mouse = Product(1);
        cart.AddItem(mouse, 99, Now);

        Assert.Throws<DomainException>(() => cart.AddItem(mouse, 2, Now));

        Assert.Equal(99, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void AddItem_Radek21_Chyba()
    {
        var cart = NewCart();
        for (var id = 1; id <= Cart.MaxLines; id++)
        {
            cart.AddItem(Product(id), 1, Now);
        }

        var ex = Assert.Throws<DomainException>(() => cart.AddItem(Product(99), 1, Now));

        Assert.Contains("20", ex.Message);
        Assert.Equal(Cart.MaxLines, cart.Lines.Count);
        cart.AddItem(Product(1), 1, Now);   // navýšení existujícího řádku limit neporušuje
    }

    [Fact]
    public void AddItem_VyprodanyNeboOdebrany_Chyba()
    {
        var cart = NewCart();
        var soldOut = Product(1);
        soldOut.ApplyAvailability(CatalogAvailability.OutOfStock, Now);
        var removed = Product(2);
        removed.MarkRemoved(Now);

        Assert.Throws<DomainException>(() => cart.AddItem(soldOut, 1, Now));
        Assert.Throws<DomainException>(() => cart.AddItem(removed, 1, Now));
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public void ChangeQuantity_HlidaRozsahAChybejiciRadek()
    {
        var cart = NewCart();
        cart.AddItem(Product(1), 1, Now);

        cart.ChangeQuantity(1, 100, Now);
        Assert.Throws<DomainException>(() => cart.ChangeQuantity(1, 0, Now));
        Assert.Throws<DomainException>(() => cart.ChangeQuantity(2, 1, Now));

        Assert.Equal(100, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void RemoveItem_AClear()
    {
        var cart = NewCart();
        cart.AddItem(Product(1), 1, Now);
        cart.AddItem(Product(2), 1, Now);

        cart.RemoveItem(1, Now);
        cart.RemoveItem(1, Now);   // podruhé (jiná záložka) – nic se nestane
        Assert.Equal(2, Assert.Single(cart.Lines).ProductId);

        cart.Clear(Now);
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public void Kosik_BezZakaznika_Chyba()
    {
        Assert.Throws<DomainException>(() => new Cart(" ", Now));
    }

    private static Cart NewCart() => new("zakaznik-jan", Now);

    private static CatalogProduct Product(int id, decimal price = 100m)
    {
        var product = new CatalogProduct(id, "Myš", price, Now);
        product.ApplyAvailability(CatalogAvailability.InStock, Now);
        return product;
    }
}
