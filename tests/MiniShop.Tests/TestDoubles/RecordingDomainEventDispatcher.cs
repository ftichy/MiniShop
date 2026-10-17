using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Testovací dispatcher – jen si zapamatuje rozeslané události.
/// </summary>
public sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
{
    public List<IDomainEvent> Dispatched { get; } = new();

    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        Dispatched.AddRange(domainEvents);
        return Task.CompletedTask;
    }
}
