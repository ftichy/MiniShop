using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Reporting;
using MiniShop.Domain.Reporting;

namespace MiniShop.Infrastructure.Reporting;

/// <summary>Proud B2: časy objednávek a záložka čtenáře proudu (schéma reporting).</summary>
public sealed class LeadTimeRepository(ReportingDbContext db, TimeProvider timeProvider) : ILeadTimeRepository
{
    public async Task<StreamCheckpoint> GetCheckpointAsync(string name, CancellationToken cancellationToken = default)
    {
        var checkpoint = await db.StreamCheckpoints.FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
        if (checkpoint is null)
        {
            checkpoint = StreamCheckpoint.Start(name, timeProvider.GetUtcNow().UtcDateTime);
            db.StreamCheckpoints.Add(checkpoint);
        }

        return checkpoint;
    }

    public async Task<IReadOnlyDictionary<int, OrderLeadTime>> GetByOrderIdsAsync(IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken = default) =>
        await db.OrderLeadTimes.Where(o => orderIds.Contains(o.OrderId)).ToDictionaryAsync(o => o.OrderId, cancellationToken);

    public async Task<IReadOnlyList<OrderLeadTime>> GetFinishedSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default) =>
        await db.OrderLeadTimes.AsNoTracking()
            .Where(o => o.ConfirmedAtUtc >= fromUtc || o.ShippedAtUtc >= fromUtc || o.DeliveredAtUtc >= fromUtc)
            .ToListAsync(cancellationToken);

    public void Add(OrderLeadTime leadTime) => db.OrderLeadTimes.Add(leadTime);

    public async Task ResetAsync(string checkpointName, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.OrderLeadTimes.ExecuteDeleteAsync(cancellationToken);
        var checkpoint = await GetCheckpointAsync(checkpointName, cancellationToken);
        checkpoint.Reset(nowUtc);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
