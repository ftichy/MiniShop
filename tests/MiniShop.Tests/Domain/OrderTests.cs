using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;

namespace MiniShop.Tests.Domain;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Order CreateOrder() => new("Jan Novák", "jan@example.com", Now);

    [Fact]
    public void TotalPrice_SectePolozkyVcetneSlevy()
    {
        var order = CreateOrder();
        order.AddItem(new Product("A", null, 100m, 10), quantity: 2, discount: 0m);
        order.AddItem(new Product("B", null, 50m, 10), quantity: 4, discount: 20m);

        Assert.Equal(200m + 180m, order.TotalPrice);
    }

    [Fact]
    public void Cancel_ZrusenouObjednavkuNelzeZrusitZnovu()
    {
        var order = CreateOrder();
        order.Cancel(Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Throws<DomainException>(() => order.Cancel(Now));
    }

    [Fact]
    public void Complete_ZrusenouObjednavkuNelzeVyridit()
    {
        var order = CreateOrder();
        order.Cancel(Now);

        Assert.Throws<DomainException>(() => order.Complete(Now));
    }
}
