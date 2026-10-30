using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

/// <summary>
/// Kanál pro oznámení zákazníkovi – od účtů kroku 5 e-mail (EmailOrderNotifier).
/// Od kroku 3 ho nevolá OrderService, ale handlery událostí (Orders/EventHandlers):
/// handler rozhoduje KDY zprávu poslat, notifier JAK ji poslat.
/// Od kroku 9 se potvrzení posílá až po rezervaci skladu (OrderConfirmed), při zamítnutí omluva (OrderRejected).
/// </summary>
public interface IOrderNotifier
{
    Task OrderConfirmedAsync(Order order, CancellationToken cancellationToken = default);
    Task OrderRejectedAsync(Order order, CancellationToken cancellationToken = default);
    Task OrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task OrderCompletedAsync(Order order, CancellationToken cancellationToken = default);
}
