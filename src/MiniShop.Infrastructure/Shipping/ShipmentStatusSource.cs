using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Domain.Shipping;
using MiniShop.Infrastructure.Observability;

namespace MiniShop.Infrastructure.Shipping;

/// <summary>
/// E-shop krok 26E: stav zásilek u dopravce pro Grafanu (měří <see cref="Hosting.OperationalMetricsWorker"/> jen na vedoucí
/// kopii Webu). Štítek jen <c>status</c> (pár hodnot), nikdy číslo zásilky ani objednávky – limit řad.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>minishop.shipment.active{status}</c> – zásilky na cestě podle stavu (každý stav i s nulou – v grafu „0“, ne díra).</item>
/// <item><c>minishop.shipment.finished{status}</c> – ukončené za 24 h (doručeno / vráceno odesílateli).</item>
/// <item><c>minishop.shipment.stale</c> – na cestě bez novinky déle než <see cref="StaleAfter"/>, nebo neznámé u dopravce
/// → alarm <c>MiniShopShipmentStale</c> (dopravce nefunguje, nebo zásilku musí vyřešit člověk v Expedici).</item>
/// </list>
/// </remarks>
public sealed class ShipmentStatusSource(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IOperationalMetricsSource
{
    /// <summary>Normální zásilka se hýbe každých 30 s a nedoručitelná je celá za ~4 min – 15 min ticha je skutečný problém.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan FinishedWindow = TimeSpan.FromHours(24);

    public string Name => "shipments";

    public async Task<IReadOnlyList<OperationalReading>> ReadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var shipments = scope.ServiceProvider.GetRequiredService<ShippingDbContext>().Shipments.AsNoTracking().IgnoreAutoIncludes();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var onTheWay = Shipment.OnTheWayStatuses;
        var finished = Shipment.FinishedStatuses;

        var active = await shipments
            .Where(s => onTheWay.Contains(s.Status))
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var finishedSince = now - FinishedWindow;
        var done = await shipments
            .Where(s => finished.Contains(s.Status) && s.LastCarrierUpdateAtUtc >= finishedSince)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var staleSince = now - StaleAfter;
        var stale = await shipments.CountAsync(s => onTheWay.Contains(s.Status)
            && (s.UnknownAtCarrier || (s.LastCarrierUpdateAtUtc ?? s.RequestedAtUtc) < staleSince), cancellationToken);

        return onTheWay
            .Select(status => Reading(OperationalMetrics.ShipmentActive, status, active.FirstOrDefault(a => a.Status == status)?.Count ?? 0))
            .Concat(finished
                .Select(status => Reading(OperationalMetrics.ShipmentFinished, status, done.FirstOrDefault(d => d.Status == status)?.Count ?? 0)))
            .Append(new OperationalReading(OperationalMetrics.ShipmentStale, stale))
            .ToList();
    }

    private static OperationalReading Reading(string metric, ShipmentStatus status, int count) =>
        new(metric, count, new KeyValuePair<string, object?>("status", status.ToString()));
}
