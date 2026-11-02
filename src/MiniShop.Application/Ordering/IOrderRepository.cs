using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

public interface IOrderRepository
{
    /// <summary>Objednávky zákazníka, nebo všechny (<paramref name="customerId"/> = null). Účty krok 3.</summary>
    Task<IReadOnlyList<Order>> GetAllAsync(string? customerId, CancellationToken cancellationToken = default);
    /// <summary>Počet objednávek a útrata za každého zákazníka (účty krok 6 – přehled uživatelů).</summary>
    Task<IReadOnlyList<CustomerOrderSummaryDto>> GetCustomerSummariesAsync(CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 4: objednávka zákazníka z daného odeslání pokladny (idempotence), nebo null.</summary>
    Task<Order?> GetByCheckoutIdAsync(string customerId, Guid checkoutId, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 4: adresa z poslední objednávky kurýrem – předvyplní pokladnu.</summary>
    Task<DeliveryAddress?> GetLastDeliveryAddressAsync(string customerId, CancellationToken cancellationToken = default);
    void Add(Order order);
}
