using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 17: košík jako event-sourced agregát, testy ve stylu event sourcingu – <b>Given</b> (dosavadní události)
/// → <b>When</b> (příkaz) → <b>Then</b> (nové události, nebo výjimka). Bez databáze.
/// </summary>
public class CartEventSourcingTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid V1 = Guid.NewGuid();
    private static readonly Guid V2 = Guid.NewGuid();

    [Fact]
    public void Given_RadekVKosiku_When_PridaniStejnehoProduktu_Then_JednaUdalostAStavZNi()
    {
        var cart = Given(new CartItemAdded(Now, V1, 1, 2, 500m));

        cart.AddItem(Product(1, 450m), 3, Now.AddMinutes(1));

        var added = Assert.IsType<CartItemAdded>(Assert.Single(cart.UncommittedEvents));
        Assert.Equal((1, 3, 450m), (added.ProductId, added.Quantity, added.Price));
        Assert.Equal((5, 450m), (Assert.Single(cart.Lines).Quantity, cart.Lines.Single().PriceWhenAdded));
        Assert.Equal(added.CheckoutId, cart.CheckoutId);   // nová verze pro pokladnu je v události
    }

    [Fact]
    public void Given_99Kusu_When_PridaniDvou_Then_VyjimkaAZadnaUdalost()
    {
        var cart = Given(new CartItemAdded(Now, V1, 1, 99, 500m));

        Assert.Throws<DomainException>(() => cart.AddItem(Product(1), 2, Now));

        Assert.Empty(cart.UncommittedEvents);   // pravidlo se hlídá PŘED událostí – neplatná se nikdy nezapíše
        Assert.Equal(99, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void Given_Historie_When_Prehrani_Then_StavJakoPoZmenach()
    {
        var cart = Given(
            new CartItemAdded(Now, V1, 1, 2, 500m),
            new CartItemAdded(Now, V1, 2, 1, 100m),
            new CartItemQuantityChanged(Now, V1, 1, 4),
            new CartCouponApplied(Now, V1, "LETO"),
            new CartItemRemoved(Now.AddMinutes(5), V2, 2));

        Assert.Equal((1, 4), (Assert.Single(cart.Lines).ProductId, cart.Lines.Single().Quantity));
        Assert.Equal(("LETO", V2, Now.AddMinutes(5), 5), (cart.CouponCode, cart.CheckoutId, cart.UpdatedAtUtc, cart.Version));
    }

    [Fact]
    public void Given_PlnyKosik_When_Objednani_Then_CheckedOutSRadkyAPrazdnyKosik()
    {
        var cart = Given(new CartItemAdded(Now, V1, 1, 2, 500m), new CartCouponApplied(Now, V1, "LETO"));

        cart.CheckOut(V1, new Dictionary<int, CatalogProduct> { [1] = Product(1, 500m) }, Now.AddMinutes(1));

        var checkedOut = Assert.IsType<CartCheckedOut>(Assert.Single(cart.UncommittedEvents));
        Assert.Equal(new CartEventLine(1, 2, 500m), Assert.Single(checkedOut.Lines));
        Assert.Empty(cart.Lines);
        Assert.Null(cart.CouponCode);   // kód patří k nákupu
    }

    [Fact]
    public void Given_OpustenyKosik_When_Pripominka_Then_VerzeProPokladnuSeNemeni()
    {
        var cart = Given(new CartItemAdded(Now, V1, 1, 1, 500m));

        cart.MarkReminderSent(Now.AddDays(2), TimeSpan.FromHours(24));

        Assert.IsType<CartReminderSent>(Assert.Single(cart.UncommittedEvents));
        Assert.Equal((V1, Now, Now.AddDays(2)), (cart.CheckoutId, cart.UpdatedAtUtc, cart.ReminderSentAtUtc));
    }

    [Fact]
    public void NacteniOdPosledniNuly_StaciUdalostiOdObjednani()
    {
        var stream = Versioned(
            new CartItemAdded(Now, V1, 1, 2, 500m),
            new CartCheckedOut(Now, V2, [new CartEventLine(1, 2, 500m)]),
            new CartItemAdded(Now, V1, 3, 1, 50m));

        var fromReset = Cart.FromLastReset(stream);

        Assert.Equal([2, 3], fromReset.Select(e => e.Version));
        var cart = Cart.FromHistory("jan", fromReset);
        Assert.Equal((3, 3), (Assert.Single(cart.Lines).ProductId, cart.Version));   // stejný stav jako z celé historie
        Assert.Equal(Cart.FromHistory("jan", stream).Lines.Select(l => l.ProductId), cart.Lines.Select(l => l.ProductId));
    }

    [Fact]
    public void KosikBezUdalosti_PrevzetiSnapshotem_ZachovaObsahIVerziProPokladnu()
    {
        var legacy = new Cart("jan", Now);
        legacy.AddItem(Product(1), 2, Now);
        legacy.MarkEventsCommitted();   // jako by byl uložený jen v tabulkách (před krokem 17)
        var checkoutId = legacy.CheckoutId;

        legacy.Rehydrate([]);

        var snapshot = Assert.IsType<CartSnapshotTaken>(Assert.Single(legacy.UncommittedEvents));
        Assert.Equal((CartSnapshotTaken.ImportedReason, checkoutId), (snapshot.Reason, snapshot.CheckoutId));
        Assert.Equal(new CartEventLine(1, 2, 100m), Assert.Single(snapshot.Lines));
        Assert.Equal(checkoutId, legacy.CheckoutId);   // otevřená pokladna dál platí
    }

    [Fact]
    public void ProjekceZmenenaBezUdalosti_Resync_ZmenaSeNeztrati()
    {
        // Projekce: 5 kusů s novou verzí (změnila ji starší verze kódu jen v tabulkách); události znají 2 kusy.
        var projection = Cart.FromHistory("jan", Versioned(new CartItemAdded(Now, V2, 1, 5, 100m)));

        projection.Rehydrate(Versioned(new CartItemAdded(Now, V1, 1, 2, 100m)));

        var snapshot = Assert.IsType<CartSnapshotTaken>(Assert.Single(projection.UncommittedEvents));
        Assert.Equal((CartSnapshotTaken.ResyncedReason, 5), (snapshot.Reason, snapshot.Lines.Single().Quantity));
        Assert.Equal(1, projection.Version);   // snapshot dostane verzi 2
    }

    private static Cart Given(params CartEvent[] history) => Cart.FromHistory("jan", Versioned(history));

    private static IReadOnlyList<VersionedCartEvent> Versioned(params CartEvent[] history) =>
        history.Select((e, i) => new VersionedCartEvent(i + 1, e)).ToList();

    private static CatalogProduct Product(int id, decimal price = 100m)
    {
        var product = new CatalogProduct(id, "Myš", price, Now);
        product.ApplyAvailability(CatalogAvailability.InStock, Now);
        return product;
    }
}
