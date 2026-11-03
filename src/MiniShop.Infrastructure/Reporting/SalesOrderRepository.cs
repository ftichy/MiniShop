using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Reporting;
using MiniShop.Domain.Reporting;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Infrastructure.Reporting;

public sealed class SalesOrderRepository(ReportingDbContext db) : ISalesOrderRepository
{
    public Task<SalesOrder?> GetAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.SalesOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<SalesOrder>> GetPlacedSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default) =>
        await db.SalesOrders
            .AsNoTracking()
            .Where(o => o.PlacedAtUtc != null && o.PlacedAtUtc >= fromUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => db.SalesOrders.AnyAsync(cancellationToken);

    public async Task<DateTime?> GetLastUpdatedAtAsync(CancellationToken cancellationToken = default) =>
        await db.SalesOrders.AnyAsync(cancellationToken)
            ? await db.SalesOrders.MaxAsync(o => o.UpdatedAtUtc, cancellationToken)
            : null;

    public void Add(SalesOrder order) => db.SalesOrders.Add(order);

    /// <summary>Načte a označí ke smazání – smaže se až v SaveChanges, v jedné transakci s žádostí o snímky.</summary>
    public async Task RemoveAllAsync(CancellationToken cancellationToken = default) =>
        db.SalesOrders.RemoveRange(await db.SalesOrders.ToListAsync(cancellationToken));
}

/// <summary>E-shop krok 11: zprávy z modulu Reporting jdou do outboxu v reporting.db.</summary>
public sealed class ReportingMessagePublisher(ReportingDbContext db)
    : OutboxIntegrationMessagePublisher(db), IReportingMessagePublisher;
