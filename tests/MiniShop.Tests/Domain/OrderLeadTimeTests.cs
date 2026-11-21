using MiniShop.Application.Reporting;
using MiniShop.Domain.Reporting;

namespace MiniShop.Tests.Domain;

/// <summary>Proud B2: časy objednávky pro Rychlost vyřízení – jen první výskyt, doručení jen u dopravce, percentily, záložka.</summary>
public sealed class OrderLeadTimeTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Doby_PotvrzeniExpediceDoruceni_OpakovanaZpravaNicNezmeni()
    {
        var order = OrderLeadTime.Create(1);
        order.ApplyStatus("Pending", T0);
        order.ApplyStatus("Confirmed", T0.AddSeconds(6));
        order.ApplyStatus("Packing", T0.AddMinutes(1));          // mezistav projekci nezajímá
        order.ApplyStatus("Shipped", T0.AddMinutes(10));
        order.ApplyStatus("Confirmed", T0.AddHours(1));          // opakovaná / pozdní zpráva – čas zůstane první
        order.ApplyStatus("Completed", T0.AddMinutes(12));

        Assert.Equal(TimeSpan.FromSeconds(6), order.Confirmation);
        Assert.Equal(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(6), order.Expedition);
        Assert.Null(order.Delivery);                              // osobní odběr / bez dopravce – čekání na zákazníka

        order.MarkByCarrier();
        Assert.Equal(TimeSpan.FromMinutes(2), order.Delivery);
    }

    [Fact]
    public void Percentily_NejblizsiPoradi()
    {
        var stats = LeadTimeReportService.Stats(Enumerable.Range(1, 20).Select(i => TimeSpan.FromSeconds(i)));

        Assert.Equal((20, 10.0, 19.0), (stats.Count, stats.P50Seconds!.Value, stats.P95Seconds!.Value));
        Assert.Equal(new LeadTimeStatsDto(0, null, null), LeadTimeReportService.Stats([]));
    }

    [Fact]
    public void Zalozka_JenDopredu_PrestaveniNovaGenerace()
    {
        var checkpoint = StreamCheckpoint.Start("lead-times", T0);
        checkpoint.Advance(10, T0, T0);
        checkpoint.Advance(5, T0, T0);   // starší offset nic nezmění

        Assert.Equal(10, checkpoint.Offset);

        checkpoint.Reset(T0);
        Assert.Null(checkpoint.Offset);
        Assert.Equal(1, checkpoint.Generation);
    }
}
