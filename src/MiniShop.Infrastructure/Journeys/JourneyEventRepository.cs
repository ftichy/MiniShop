using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Journeys;
using MiniShop.Domain.Journeys;

namespace MiniShop.Infrastructure.Journeys;

public sealed class JourneyEventRepository(JourneysDbContext db) : IJourneyEventRepository
{
    public async Task<IReadOnlySet<Guid>> GetExistingEventIdsAsync(IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken = default) =>
        (await db.JourneyEvents
            .Where(e => eventIds.Contains(e.EventId))
            .Select(e => e.EventId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public async Task<Guid?> FindJourneyIdByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        await db.JourneyEvents
            .Where(e => e.OrderId == orderId && e.Type == JourneyEventType.OrderSubmitted)
            .Select(e => (Guid?)e.JourneyId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<JourneyEvent>> GetJourneyAsync(Guid journeyId, CancellationToken cancellationToken = default) =>
        await db.JourneyEvents
            .AsNoTracking()
            .Where(e => e.JourneyId == journeyId)
            .OrderBy(e => e.OffsetMs).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    public void AddRange(IEnumerable<JourneyEvent> events) => db.JourneyEvents.AddRange(events);
}
