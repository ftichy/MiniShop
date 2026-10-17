using MiniShop.Domain.Common;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Z řádku outboxu druhu Domain znovu sestaví doménovou událost (krok 5). Implementuje modul, který doménové
/// události přes outbox posílá (dnes jen Ordering – OrderingDomainEventReader).
/// </summary>
public interface IDomainEventReader
{
    Task<IDomainEvent> ReadAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
