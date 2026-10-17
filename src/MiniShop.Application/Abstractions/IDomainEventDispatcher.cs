using MiniShop.Domain.Common;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Doručí doménové události všem handlerům, kteří se o ně zajímají (Publish/Subscribe v rámci procesu).
/// </summary>
/// <remarks>
/// Chyba jednoho handleru nezastaví ostatní. Když ale některý selže, DispatchAsync na konci vyhodí
/// AggregateException – volající (outbox, krok 4) tak ví, že událost není doručená celá.
/// </remarks>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
