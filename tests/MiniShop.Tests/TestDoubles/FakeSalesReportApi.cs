using MiniShop.Application.Reporting;
using MiniShop.Web.Reporting;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Mikroservisy M1: služba přehledu tržeb v testech Webu – Reporting.Api neběží. Vrací pevný přehled, počítá žádosti
/// o přestavění a umí „spadnout“ (<see cref="Available"/>), aby šlo ověřit, že stránka výpadek ustojí.
/// </summary>
public sealed class FakeSalesReportApi : ISalesReportApi, ILeadTimeApi
{
    private int _leadTimeRebuilds;

    /// <summary>Proud B2: kolikrát stránka Rychlost vyřízení požádala o přestavění z proudu.</summary>
    public int LeadTimeRebuildRequests => _leadTimeRebuilds;

    public Task<LeadTimeReportDto> GetLeadTimesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var stats = new LeadTimeStatsDto(3, 6.5, 12.0);
        var none = new LeadTimeStatsDto(0, null, null);
        return Task.FromResult(new LeadTimeReportDto([new LeadTimeDayDto(new DateOnly(2026, 11, 21), stats, none, none)],
            stats, none, none, Offset: 41, LastMessageAtUtc: new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc), Generation: 2));
    }

    public Task RebuildLeadTimesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        Interlocked.Increment(ref _leadTimeRebuilds);
        return Task.CompletedTask;
    }

    private int _rebuildRequests;

    public bool Available { get; set; } = true;

    public int RebuildRequests => _rebuildRequests;

    public Task<SalesReportDto> GetAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var today = new SalesDayDto(new DateOnly(2026, 11, 17), 2, 1_250m);
        return Task.FromResult(new SalesReportDto(today, [today], [new TopProductDto(1, "Testovací produkt", 3, 300m)],
            new Dictionary<string, int> { ["Confirmed"] = 2, ["Returned"] = 0 }, 0m, null));
    }

    public Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        Interlocked.Increment(ref _rebuildRequests);
        return Task.CompletedTask;
    }

    private void ThrowIfUnavailable()
    {
        if (!Available)
        {
            throw new SalesReportUnavailableException(new HttpRequestException("reporting-api neběží"));
        }
    }
}
