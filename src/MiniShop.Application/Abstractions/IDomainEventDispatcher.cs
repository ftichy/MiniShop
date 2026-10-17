using MiniShop.Domain.Common;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Doručí doménové události všem handlerům, kteří se o ně zajímají (Publish/Subscribe v rámci procesu).
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
