using MiniShop.Domain.Reporting;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 11: řádek čtecího modelu přehledu tržeb – zprávy v libovolném pořadí dají stejný výsledek
/// (stav jen dopředu, konečný stav vyhraje, detaily doplní vznik).
/// </summary>
public class SalesOrderTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(SalesOrderStatus.Confirmed, SalesOrderStatus.Placed, SalesOrderStatus.Confirmed)]      // pozdní „přijato“
    [InlineData(SalesOrderStatus.Cancelled, SalesOrderStatus.Confirmed, SalesOrderStatus.Cancelled)]   // pozdní potvrzení
    [InlineData(SalesOrderStatus.Completed, SalesOrderStatus.Cancelled, SalesOrderStatus.Completed)]   // konečný zůstává
    [InlineData(SalesOrderStatus.Confirmed, SalesOrderStatus.Completed, SalesOrderStatus.Completed)]   // dopředu ano
    public void Stav_JenDopredu(SalesOrderStatus first, SalesOrderStatus second, SalesOrderStatus expected)
    {
        var order = SalesOrder.Create(1, Now);
        order.ApplyStatus(first, Now);
        order.ApplyStatus(second, Now);

        Assert.Equal(expected, order.Status);
    }

    [Fact]
    public void Trzba_AzSDetailyANeZrusena()
    {
        var order = SalesOrder.Create(1, Now);
        order.ApplyStatus(SalesOrderStatus.Confirmed, Now);
        Assert.False(order.CountsAsRevenue);   // potvrzení předběhlo vznik – nevíme kdy ani kolik

        order.ApplyPlaced(Now, 250m, 0m, [new SalesOrderLine(7, "Myš", 1, 250m)], Now);
        Assert.True(order.CountsAsRevenue);

        order.ApplyStatus(SalesOrderStatus.Cancelled, Now);
        Assert.False(order.CountsAsRevenue);
    }

    [Fact]
    public void Detaily_PodruhePrepisiRadky()
    {
        var order = SalesOrder.Create(1, Now);
        order.ApplyPlaced(Now, 100m, 0m, [new SalesOrderLine(7, "Myš", 1, 100m)], Now);
        order.ApplyPlaced(Now, 100m, 0m, [new SalesOrderLine(7, "Myš", 1, 100m)], Now);   // vznik + snímek

        Assert.Single(order.Lines);
    }
}
