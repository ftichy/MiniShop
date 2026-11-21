using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Tests.TestDoubles;

/// <summary>Proud B1: proud zpráv v paměti pro testy stránky – zprávy, filtr jako skutečná čtečka, nebo výpadek.</summary>
public sealed class FakeEventStreamReader : IEventStreamReader
{
    public List<EventStreamEntry> Entries { get; } = [];

    public bool Unavailable { get; set; }

    public EventStreamQuery? LastQuery { get; private set; }

    public Task<EventStreamResult> ReadAsync(EventStreamQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        if (Unavailable)
        {
            throw new EventStreamUnavailableException("RabbitMQ není dostupný.");
        }

        var matched = Entries
            .Where(e => e.OccurredAtUtc >= query.FromUtc
                        && (string.IsNullOrEmpty(query.Type) || e.Type == query.Type)
                        && (query.OrderId is null || e.OrderId == query.OrderId))
            .OrderBy(e => e.Offset)
            .ToList();
        return Task.FromResult(new EventStreamResult(matched, false, Entries.Count));
    }
}
