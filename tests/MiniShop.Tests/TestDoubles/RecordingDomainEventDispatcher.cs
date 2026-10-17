using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Testovací dispatcher – zapamatuje si rozeslané události a volitelně je předá skutečnému dispatcheru.
/// </summary>
public sealed class RecordingDomainEventDispatcher(IDomainEventDispatcher? inner = null) : IDomainEventDispatcher
{
    public List<IDomainEvent> Dispatched { get; } = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        var list = domainEvents.ToList();
        Dispatched.AddRange(list);

        if (inner is not null)
        {
            await inner.DispatchAsync(list, cancellationToken);
        }
    }
}
