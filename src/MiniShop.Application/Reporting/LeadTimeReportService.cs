using MiniShop.Application.Abstractions;
using MiniShop.Domain.Reporting;

namespace MiniShop.Application.Reporting;

/// <summary>Proud B2: časy objednávek a záložka čtenáře proudu v databázi modulu Reporting.</summary>
public interface ILeadTimeRepository
{
    /// <summary>Záložka projekce – založí ji, když ještě není (přidá ke změnám, uloží až unit of work).</summary>
    Task<StreamCheckpoint> GetCheckpointAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, OrderLeadTime>> GetByOrderIdsAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);

    /// <summary>Objednávky, u kterých se od <paramref name="fromUtc"/> něco dokončilo (potvrzení, odeslání, doručení).</summary>
    Task<IReadOnlyList<OrderLeadTime>> GetFinishedSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default);

    void Add(OrderLeadTime leadTime);

    /// <summary>
    /// Přestavění: v JEDNÉ transakci smazat všechny časy a záložku přetočit na začátek (nová generace). Souběžná dávka
    /// čtenáře pak neprojde (concurrency token záložky) a čtenář začne znovu od začátku proudu.
    /// </summary>
    Task ResetAsync(string checkpointName, DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Počet a percentily jedné doby (sekundy; null = žádná objednávka).</summary>
public sealed record LeadTimeStatsDto(int Count, double? P50Seconds, double? P95Seconds);

public sealed record LeadTimeDayDto(DateOnly Day, LeadTimeStatsDto Confirmation, LeadTimeStatsDto Expedition, LeadTimeStatsDto Delivery);

/// <summary>
/// Přehled Rychlost vyřízení + odkud data jsou: záložka v proudu (<paramref name="Offset"/>), čas poslední zprávy,
/// generace (kolikrát se přestavovalo).
/// </summary>
public sealed record LeadTimeReportDto(
    IReadOnlyList<LeadTimeDayDto> Days,
    LeadTimeStatsDto Confirmation,
    LeadTimeStatsDto Expedition,
    LeadTimeStatsDto Delivery,
    long? Offset,
    DateTime? LastMessageAtUtc,
    int Generation);

/// <summary>
/// Proud B2: přehled <b>Rychlost vyřízení</b> – jak dlouho trvá potvrzení, expedice a doručení (p50 a p95) po dnech
/// v českém čase, podle dne, kdy krok skončil. Data plní jen přehrávání proudu zpráv (čtenář v Reporting.Api).
/// </summary>
public sealed class LeadTimeReportService(ILeadTimeRepository leadTimes, TimeProvider timeProvider)
{
    /// <summary>Jméno záložky v tabulce StreamCheckpoints.</summary>
    public const string CheckpointName = "lead-times";

    public const int Days = 14;

    public async Task<LeadTimeReportDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(CzechTime.FromUtc(timeProvider.GetUtcNow().UtcDateTime));
        var firstDay = today.AddDays(-(Days - 1));
        var fromUtc = CzechTime.ToUtc(firstDay.ToDateTime(TimeOnly.MinValue));
        var orders = await leadTimes.GetFinishedSinceAsync(fromUtc, cancellationToken);
        var checkpoint = await leadTimes.GetCheckpointAsync(CheckpointName, cancellationToken);

        static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(CzechTime.FromUtc(utc));

        // Doba podle dne, kdy krok SKONČIL (potvrzení – den potvrzení…), jen v okně přehledu.
        List<(DateOnly Day, TimeSpan Duration)> Durations(Func<OrderLeadTime, TimeSpan?> duration, Func<OrderLeadTime, DateTime?> end) =>
            orders.Where(o => duration(o) is not null && end(o) is { } at && at >= fromUtc)
                .Select(o => (Day(end(o)!.Value), duration(o)!.Value))
                .ToList();

        var confirmation = Durations(o => o.Confirmation, o => o.ConfirmedAtUtc);
        var expedition = Durations(o => o.Expedition, o => o.ShippedAtUtc);
        var delivery = Durations(o => o.Delivery, o => o.DeliveredAtUtc);

        var days = Enumerable.Range(0, Days)
            .Select(i => today.AddDays(-i))
            .Select(day => new LeadTimeDayDto(day,
                Stats(confirmation.Where(d => d.Day == day).Select(d => d.Duration)),
                Stats(expedition.Where(d => d.Day == day).Select(d => d.Duration)),
                Stats(delivery.Where(d => d.Day == day).Select(d => d.Duration))))
            .ToList();

        return new LeadTimeReportDto(days,
            Stats(confirmation.Select(d => d.Duration)), Stats(expedition.Select(d => d.Duration)), Stats(delivery.Select(d => d.Duration)),
            checkpoint.Offset, checkpoint.LastMessageAtUtc, checkpoint.Generation);
    }

    /// <summary>
    /// Přestavění = smazat data a záložku přetočit na začátek proudu (nová generace) – v jedné transakci. Čtenář proudu to
    /// pozná a přehraje celou historii znovu (jen v rámci retence proudu – 14 dní). Ordering se na nic neptá.
    /// </summary>
    public Task RebuildAsync(CancellationToken cancellationToken = default) =>
        leadTimes.ResetAsync(CheckpointName, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    /// <summary>Počet, medián (p50) a p95 – metoda nejbližšího pořadí.</summary>
    public static LeadTimeStatsDto Stats(IEnumerable<TimeSpan> durations)
    {
        var sorted = durations.Select(d => d.TotalSeconds).Order().ToList();
        return sorted.Count == 0
            ? new LeadTimeStatsDto(0, null, null)
            : new LeadTimeStatsDto(sorted.Count, Percentile(sorted, 0.5), Percentile(sorted, 0.95));
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
}
