using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Tests.Domain;

/// <summary>
/// Krok 1 EDA: entity vyvolávají doménové události při změně stavu.
/// </summary>
public class OrderDomainEventsTests
{
    private static readonly DateTime CreatedAt = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = CreatedAt.AddHours(2);

    private static Order CreateOrder() => new("Jan Novák", "jan@example.com", CreatedAt);

    [Fact]
    public void NovaObjednavka_VyvolaOrderPlaced()
    {
        var order = CreateOrder();

        var placed = Assert.IsType<OrderPlaced>(Assert.Single(order.DomainEvents));
        Assert.Same(order, placed.Order);
        Assert.Equal(CreatedAt, placed.OccurredAtUtc);
    }

    [Fact]
    public void Cancel_VyvolaPraveJednuUdalostOrderCancelled()
    {
        var order = CreateOrder();
        order.ClearDomainEvents();

        order.Cancel(Later);

        var cancelled = Assert.IsType<OrderCancelled>(Assert.Single(order.DomainEvents));
        Assert.Equal(Later, cancelled.OccurredAtUtc);
    }

    [Fact]
    public void Complete_VyvolaOrderCompleted()
    {
        var order = CreateOrder();
        order.ClearDomainEvents();

        order.Complete(Later);

        Assert.IsType<OrderCompleted>(Assert.Single(order.DomainEvents));
    }

    [Fact]
    public void NeplatnaZmenaStavu_NevyvolaZadnouUdalost()
    {
        var order = CreateOrder();
        order.Cancel(Later);
        order.ClearDomainEvents();

        Assert.Throws<DomainException>(() => order.Cancel(Later));

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void ClearDomainEvents_VyprazdniSeznam()
    {
        var order = CreateOrder();

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
    }
}
