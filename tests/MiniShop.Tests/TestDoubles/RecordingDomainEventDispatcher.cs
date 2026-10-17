using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Testovací dispatcher – zapíše rozeslané události do sdíleného seznamu a volitelně je předá skutečnému dispatcheru.
/// Seznam je sdílený, protože dispatcher je scoped a každá zpráva z outboxu dostane vlastní instanci.
/// </summary>
public sealed class RecordingDomainEventDispatcher(List<IDomainEvent> dispatched, IDomainEventDispatcher? inner = null)
    : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        var list = domainEvents.ToList();
        dispatched.AddRange(list);

        if (inner is not null)
        {
            await inner.DispatchAsync(list, cancellationToken);
        }
    }
}
