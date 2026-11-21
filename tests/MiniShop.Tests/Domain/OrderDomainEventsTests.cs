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

    private static Order CreateOrder() => new("zakaznik-1", "Jan Novák", "jan@example.com", CreatedAt);

    /// <summary>Události s vlastním významem – bez obecné OrderStatusChanged (vzhled krok F3a), kterou má každá změna stavu.</summary>
    private static IEnumerable<MiniShop.Domain.Common.IDomainEvent> Business(Order order) =>
        order.DomainEvents.Where(e => e is not OrderStatusChanged);

    private static Order CreateConfirmedOrder()
    {
        var order = CreateOrder();
        order.Confirm(CreatedAt);
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public void NovaObjednavka_VyvolaOrderPlaced()
    {
        var order = CreateOrder();

        var placed = Assert.IsType<OrderPlaced>(Assert.Single(Business(order)));
        Assert.Same(order, placed.Order);
        Assert.Equal(CreatedAt, placed.OccurredAtUtc);
    }

    [Fact]
    public void Confirm_VyvolaOrderConfirmed()
    {
        var order = CreateOrder();
        order.ClearDomainEvents();

        order.Confirm(Later);

        var confirmed = Assert.IsType<OrderConfirmed>(Assert.Single(Business(order)));
        Assert.Equal(Later, confirmed.OccurredAtUtc);
    }

    [Fact]
    public void Reject_VyvolaOrderRejected()
    {
        var order = CreateOrder();
        order.ClearDomainEvents();

        order.Reject("Nedostatek zboží", Later);

        Assert.IsType<OrderRejected>(Assert.Single(Business(order)));
    }

    [Fact]
    public void Cancel_VyvolaPraveJednuUdalostOrderCancelled()
    {
        var order = CreateConfirmedOrder();

        order.Cancel(Later);

        var cancelled = Assert.IsType<OrderCancelled>(Assert.Single(Business(order)));
        Assert.Equal(Later, cancelled.OccurredAtUtc);
    }

    [Fact]
    public void Expedice_KazdyKrokVyvolaSvouUdalost()
    {
        var order = CreateConfirmedOrder();

        order.StartPacking(Later);
        order.Ship(null, null, Later);   // objednávka bez dopravy – dopravce nepovinný
        order.MarkDelivered(Later);

        // E-shop krok 6: doručení dál vyvolá OrderCompleted (kvůli kontraktu OrderCompletedV1).
        Assert.Equal([typeof(OrderPackingStarted), typeof(OrderShipped), typeof(OrderCompleted)],
            Business(order).Select(e => e.GetType()));
    }

    /// <summary>Vzhled krok F3a: každá změna stavu (i bez vlastní události – balení) vyvolá i obecnou OrderStatusChanged.</summary>
    [Fact]
    public void KazdaZmenaStavu_VyvolaOrderStatusChanged()
    {
        var order = CreateOrder();
        Assert.Single(order.DomainEvents.OfType<OrderStatusChanged>());   // vznik = Pending
        order.ClearDomainEvents();

        order.Confirm(Later);
        order.StartPacking(Later);

        Assert.Equal(2, order.DomainEvents.OfType<OrderStatusChanged>().Count());
        Assert.All(order.DomainEvents.OfType<OrderStatusChanged>(), e => Assert.Same(order, e.Order));
    }

    [Fact]
    public void NeplatnaZmenaStavu_NevyvolaZadnouUdalost()
    {
        var order = CreateConfirmedOrder();
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
