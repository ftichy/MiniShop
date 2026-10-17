using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Testovací náhrada notifikací – jen si zapamatuje, co se stalo (díky DIP ji snadno podstrčíme).
/// </summary>
public sealed class RecordingOrderNotifier : IOrderNotifier
{
    public List<string> Events { get; } = new();

    /// <summary>Kolikrát má oznámení o zrušení ještě selhat – simuluje nedostupný e-mailový server.</summary>
    public int CancellationFailuresLeft { get; set; }

    public Task OrderConfirmedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Events.Add($"confirmed:{order.Id}");
        return Task.CompletedTask;
    }

    public Task OrderRejectedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Events.Add($"rejected:{order.Id}");
        return Task.CompletedTask;
    }

    public Task OrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (CancellationFailuresLeft > 0)
        {
            CancellationFailuresLeft--;
            throw new InvalidOperationException("E-mailový server nedostupný");
        }

        Events.Add($"cancelled:{order.Id}");
        return Task.CompletedTask;
    }

    public Task OrderCompletedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Events.Add($"completed:{order.Id}");
        return Task.CompletedTask;
    }
}
