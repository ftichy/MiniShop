using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Coupons;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 13: pravidla slevového kódu (platnost, minimum, limit, verze) a rozpočítání slevy do položek objednávky.
/// </summary>
public class CouponTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

    private static Coupon Percent(int percent = 10, int? max = null, decimal min = 0, DateTime? until = null) =>
        Coupon.Create("jaro-10", CouponKind.Percent, percent, until, min, max, Now);

    [Theory]
    [InlineData("ab", CouponKind.Percent, 10)]          // krátký kód
    [InlineData("ŠKODA", CouponKind.Percent, 10)]       // diakritika
    [InlineData("JARO", CouponKind.Percent, 95)]        // víc než 90 %
    [InlineData("JARO", CouponKind.Amount, 0)]          // nulová sleva
    public void Zalozeni_NeplatneHodnoty(string code, CouponKind kind, decimal value)
    {
        Assert.Throws<DomainException>(() => Coupon.Create(code, kind, value, null, 0, null, Now));
    }

    [Fact]
    public void Kod_SeUkladaVelkymiPismeny()
    {
        Assert.Equal("JARO-10", Percent().Code);
        Assert.Equal("JARO-10", Coupon.NormalizeCode("  jaro-10 "));
    }

    [Fact]
    public void Pouziti_LimitPlatnostMinimum()
    {
        var coupon = Percent(max: 1, min: 500, until: Now.AddDays(1));

        Assert.Contains("od 500", coupon.WhyNotApplicable(400, Now));
        Assert.Contains("Platnost", coupon.WhyNotApplicable(600, Now.AddDays(2)));
        Assert.Equal(60m, coupon.Redeem(600, Now));
        Assert.Equal((1, 1), (coupon.RedemptionCount, coupon.Version));
        Assert.Contains("vyčerpaný", coupon.WhyNotApplicable(600, Now));
        Assert.Throws<DomainException>(() => coupon.Redeem(600, Now));

        coupon.Release();   // objednávka zamítnuta → použití zpět
        Assert.Null(coupon.WhyNotApplicable(600, Now));
        Assert.Equal(2, coupon.Version);
    }

    [Fact]
    public void Sleva_VKcNikdyNepresahneZbozi()
    {
        var coupon = Coupon.Create("MINUS500", CouponKind.Amount, 500, null, 0, null, Now);

        Assert.Equal(200m, coupon.DiscountFor(200));
        Assert.Equal(500m, coupon.DiscountFor(1200));
    }

    [Fact]
    public void Vypnuty_NejdePouzit()
    {
        var coupon = Percent();
        coupon.Deactivate();

        Assert.NotNull(coupon.WhyNotApplicable(1000, Now));
    }

    // --- rozpočítání do položek objednávky ---

    private static Order OrderWith(params decimal[] prices)
    {
        var order = new Order("zakaznik-1", "Jan", "jan@example.com", Now);
        for (var i = 0; i < prices.Length; i++)
        {
            var product = new CatalogProduct(i + 1, $"Produkt {i + 1}", prices[i], Now);
            order.AddItem(product, 1, 0m);
        }

        return order;
    }

    [Fact]
    public void Objednavka_SlevaRozpocitanaPresneNaHalere()
    {
        var order = OrderWith(100m, 100m, 100m);   // 10 Kč na tři stejné řádky → 3,33 + 3,33 + 3,34
        order.ApplyCoupon(Coupon.Create("TRETINA", CouponKind.Amount, 10, null, 0, null, Now), Now);

        Assert.Equal(290m, order.ItemsTotal);
        Assert.Equal(10m, order.CouponDiscount);
        Assert.Equal(10m, order.Items.Sum(i => i.Discount));
        Assert.All(order.Items, i => Assert.InRange(i.Discount, 3.33m, 3.34m));
    }

    [Fact]
    public void Objednavka_SlevaCeleZbozi_ZadnyRadekPodNulu_JenJedenKod()
    {
        var order = OrderWith(0.01m, 0.02m, 99.97m);
        order.ApplyCoupon(Coupon.Create("VSECHNO", CouponKind.Amount, 1000, null, 0, null, Now), Now);

        Assert.Equal(0m, order.ItemsTotal);
        Assert.All(order.Items, i => Assert.Equal(0m, i.LineTotal));
        Assert.Throws<DomainException>(() => order.ApplyCoupon(Percent(), Now));
    }
}
