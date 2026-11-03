using MiniShop.Contracts.Reporting;
using MiniShop.Domain.Reporting;

namespace MiniShop.Application.Reporting;

/// <summary>
/// E-shop krok 11 (CQRS): čtecí strana – přehled tržeb pro admina počítaný jen z čtecího modelu (reporting.db),
/// bez dotazu do Orderingu. A přestavění: smazat čtecí model a nechat si poslat všechny objednávky znovu.
/// </summary>
/// <remarks>
/// Dny se počítají v českém čase (objednávka v 0:30 patří do „dneška“, i když v UTC je to včera).
/// Součty v paměti – SQLite neumí sčítat decimal přesně a dat je málo (posledních 30 dní).
/// </remarks>
public sealed class SalesReportService(
    ISalesOrderRepository orders,
    IReportingMessagePublisher publisher,
    IReportingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int DaysInChart = 14;
    public const int DaysInTopProducts = 30;
    public const int TopProductsCount = 10;

    private static readonly TimeZoneInfo Czech = FindCzechTimeZone();

    public async Task<SalesReportDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = UtcNow;
        var today = DateOnly.FromDateTime(ToCzech(nowUtc));
        var from = today.AddDays(-(DaysInTopProducts - 1));
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), Czech);

        var recent = await orders.GetPlacedSinceAsync(fromUtc, cancellationToken);
        var byDay = recent.ToLookup(o => DateOnly.FromDateTime(ToCzech(o.PlacedAtUtc!.Value)));

        var days = Enumerable.Range(0, DaysInChart)
            .Select(offset => today.AddDays(-offset))
            .Select(day => ToDay(day, byDay[day]))
            .ToList();

        var topProducts = recent
            .Where(o => o.CountsAsRevenue)
            .SelectMany(o => o.Lines)
            .GroupBy(l => l.ProductId)
            .Select(g => new TopProductDto(g.Key, g.Last().ProductName, g.Sum(l => l.Quantity), g.Sum(l => l.LineTotal)))
            .OrderByDescending(p => p.Quantity)
            .ThenByDescending(p => p.Revenue)
            .Take(TopProductsCount)
            .ToList();

        var statusCounts = Enum.GetValues<SalesOrderStatus>()
            .ToDictionary(status => status, status => recent.Count(o => o.Status == status));

        return new SalesReportDto(
            days[0],
            days,
            topProducts,
            statusCounts,
            recent.Sum(o => o.RefundedAmount),
            await orders.GetLastUpdatedAtAsync(cancellationToken));
    }

    /// <summary>
    /// Zahodit čtecí model a postavit ho znovu: smazání a žádost o snímky (<see cref="RebuildSalesReportV1"/>)
    /// v jedné transakci. Ordering pošle snímek každé objednávky, projekce je zpracuje jako jiné zprávy.
    /// </summary>
    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        await orders.RemoveAllAsync(cancellationToken);
        publisher.Send(new RebuildSalesReportV1(Guid.NewGuid(), UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Po startu: prázdný přehled (nová reporting.db – první nasazení) se postaví ze snímků.</summary>
    /// <returns>true, když o přestavění požádala.</returns>
    public async Task<bool> RebuildIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (await orders.AnyAsync(cancellationToken))
        {
            return false;
        }

        await RebuildAsync(cancellationToken);
        return true;
    }

    private static SalesDayDto ToDay(DateOnly day, IEnumerable<SalesOrder> placed)
    {
        var counted = placed.Where(o => o.CountsAsRevenue).ToList();
        return new SalesDayDto(day, counted.Count, counted.Sum(o => o.TotalPrice));
    }

    private static DateTime ToCzech(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Czech);

    /// <summary>IANA jméno funguje na Linuxu i na Windows (.NET převod přes ICU); bez časových zón (minimální image) UTC.</summary>
    private static TimeZoneInfo FindCzechTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

/// <param name="Today">Dnešek (první z <paramref name="Days"/>).</param>
/// <param name="Days">Posledních 14 dní, nejnovější první.</param>
/// <param name="StatusCounts">Počet objednávek podle stavu za 30 dní.</param>
/// <param name="RefundedAmount">Vrácené peníze za 30 dní (do tržby se nepočítají).</param>
/// <param name="LastUpdatedAtUtc">Kdy projekce naposledy zpracovala událost (null = přehled je prázdný).</param>
public sealed record SalesReportDto(
    SalesDayDto Today,
    IReadOnlyList<SalesDayDto> Days,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyDictionary<SalesOrderStatus, int> StatusCounts,
    decimal RefundedAmount,
    DateTime? LastUpdatedAtUtc);

/// <param name="OrderCount">Objednávky, které nejsou zamítnuté ani zrušené.</param>
/// <param name="Revenue">Jejich součet včetně dopravy.</param>
public sealed record SalesDayDto(DateOnly Day, int OrderCount, decimal Revenue);

public sealed record TopProductDto(int ProductId, string Name, int Quantity, decimal Revenue);
