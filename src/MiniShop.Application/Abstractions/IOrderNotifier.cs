using MiniShop.Domain.Orders;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Kanál pro oznámení zákazníkovi (dnes log, později třeba e-mail).
/// Od kroku 3 ho nevolá OrderService, ale handlery událostí (Orders/EventHandlers):
/// handler rozhoduje KDY zprávu poslat, notifier JAK ji poslat.
/// </summary>
public interface IOrderNotifier
{
    Task OrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task OrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task OrderCompletedAsync(Order order, CancellationToken cancellationToken = default);
}
