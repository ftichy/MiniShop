using MiniShop.Domain.Orders;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Oznámení o změnách objednávek (e-mail, log, ...).
/// Při přechodu na EDA se z tohoto místa stane publikování událostí (OrderPlaced, OrderCancelled).
/// </summary>
public interface IOrderNotifier
{
    Task OrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task OrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task OrderCompletedAsync(Order order, CancellationToken cancellationToken = default);
}
