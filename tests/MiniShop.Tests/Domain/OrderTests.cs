using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Domain;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Order CreateOrder() => new("Jan Novák", "jan@example.com", Now);

    private static Order CreateConfirmedOrder()
    {
        var order = CreateOrder();
        order.Confirm(Now);
        return order;
    }

    [Fact]
    public void TotalPrice_SectePolozkyVcetneSlevy()
    {
        var order = CreateOrder();
        order.AddItem(new CatalogProduct(1, "A", 100m, Now), quantity: 2, discount: 0m);
        order.AddItem(new CatalogProduct(2, "B", 50m, Now), quantity: 4, discount: 20m);

        Assert.Equal(300m + 180m, order.TotalPrice);
    }

    [Fact]
    public void NovaObjednavka_CekaNaSklad()
    {
        Assert.Equal(OrderStatus.Pending, CreateOrder().Status);
    }

    [Fact]
    public void Reject_UloziDuvod()
    {
        var order = CreateOrder();

        order.Reject("Nedostatek zboží", Now);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Nedostatek zboží", order.RejectionReason);
    }

    [Fact]
    public void Reject_BezDuvodu_VyhodiVyjimku()
    {
        Assert.Throws<DomainException>(() => CreateOrder().Reject(" ", Now));
    }

    [Fact]
    public void Confirm_ZamitnutouObjednavkuNelzePotvrdit()
    {
        var order = CreateOrder();
        order.Reject("Nedostatek zboží", Now);

        Assert.Throws<DomainException>(() => order.Confirm(Now));
    }

    [Fact]
    public void CekajiciObjednavku_NelzeZrusitAniVyridit()
    {
        var order = CreateOrder();

        Assert.Throws<DomainException>(() => order.Cancel(Now));
        Assert.Throws<DomainException>(() => order.Complete(Now));
    }

    [Fact]
    public void Cancel_ZrusenouObjednavkuNelzeZrusitZnovu()
    {
        var order = CreateConfirmedOrder();
        order.Cancel(Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Throws<DomainException>(() => order.Cancel(Now));
    }

    [Fact]
    public void Complete_ZrusenouObjednavkuNelzeVyridit()
    {
        var order = CreateConfirmedOrder();
        order.Cancel(Now);

        Assert.Throws<DomainException>(() => order.Complete(Now));
    }

    [Fact]
    public void AddItem_DoPotvrzeneObjednavkyNelze()
    {
        var order = CreateConfirmedOrder();

        Assert.Throws<DomainException>(() => order.AddItem(new CatalogProduct(1, "A", 100m, Now), 1, 0m));
    }
}
