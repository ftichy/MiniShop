using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    void Add(Order order);
}
