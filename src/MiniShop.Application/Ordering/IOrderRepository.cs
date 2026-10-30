using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

public interface IOrderRepository
{
    /// <summary>Objednávky zákazníka, nebo všechny (<paramref name="customerId"/> = null). Účty krok 3.</summary>
    Task<IReadOnlyList<Order>> GetAllAsync(string? customerId, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    void Add(Order order);
}
