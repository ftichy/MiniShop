using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Testovací náhrada notifikací – jen si zapamatuje, co se stalo (díky DIP ji snadno podstrčíme).
/// </summary>
public sealed class RecordingOrderNotifier : IOrderNotifier
{
    public List<string> Events { get; } = new();

    public Task OrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Events.Add($"placed:{order.Id}");
        return Task.CompletedTask;
    }

    public Task OrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Events.Add($"cancelled:{order.Id}");
        return Task.CompletedTask;
    }

    public Task OrderCompletedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Events.Add($"completed:{order.Id}");
        return Task.CompletedTask;
    }
}
