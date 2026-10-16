using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;

namespace MiniShop.Tests.Domain;

public class OrderTests
{
    private static Order CreateOrder() => new("Jan Novák", "jan@example.com", DateTime.UtcNow);

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
        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Throws<DomainException>(order.Cancel);
    }

    [Fact]
    public void Complete_ZrusenouObjednavkuNelzeVyridit()
    {
        var order = CreateOrder();
        order.Cancel();

        Assert.Throws<DomainException>(order.Complete);
    }
}
