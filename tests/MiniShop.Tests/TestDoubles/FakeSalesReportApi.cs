using MiniShop.Application.Reporting;
using MiniShop.Web.Reporting;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Mikroservisy M1: služba přehledu tržeb v testech Webu – Reporting.Api neběží. Vrací pevný přehled, počítá žádosti
/// o přestavění a umí „spadnout“ (<see cref="Available"/>), aby šlo ověřit, že stránka výpadek ustojí.
/// </summary>
public sealed class FakeSalesReportApi : ISalesReportApi
{
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
