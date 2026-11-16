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
    [InlineData(SalesOrderStatus.Completed, SalesOrderStatus.Returned, SalesOrderStatus.Returned)]     // e-shop krok 12: vratka
    [InlineData(SalesOrderStatus.Returned, SalesOrderStatus.Completed, SalesOrderStatus.Returned)]     // pozdní „doručeno“
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

    /// <summary>E-shop krok 22: vratka po částech – stav zůstává, z tržby a top produktů se odečte jen vrácené, každá vratka jednou.</summary>
    [Fact]
    public void VratkaPoCastech_OdectiJenVraceneAJednou()
    {
        var order = SalesOrder.Create(1, Now);
        order.ApplyPlaced(Now, 349m, 49m, [new SalesOrderLine(7, "Myš", 2, 200m), new SalesOrderLine(8, "Klávesnice", 1, 100m)], Now);
        order.ApplyStatus(SalesOrderStatus.Completed, Now);

        Assert.True(order.ApplyReturn(1, 100m, [new SalesOrderReturnLine(1, 7, 1, 100m)], Now));
        Assert.False(order.ApplyReturn(1, 100m, [new SalesOrderReturnLine(1, 7, 1, 100m)], Now));   // stejná vratka podruhé
        Assert.True(order.ApplyReturn(2, 100m, [new SalesOrderReturnLine(2, 8, 1, 100m)], Now));

        Assert.Equal(SalesOrderStatus.Completed, order.Status);
        Assert.True(order.CountsAsRevenue);
        Assert.Equal((200m, 149m), (order.ReturnedAmount, order.Revenue));
        Assert.Equal([new SalesOrderLine(7, "Myš", 1, 100m)], order.KeptLines());   // klávesnice vrácená celá zmizí
    }
}
