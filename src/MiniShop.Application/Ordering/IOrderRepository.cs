using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

public interface IOrderRepository
{
    /// <summary>Objednávky zákazníka, nebo všechny (<paramref name="customerId"/> = null). Účty krok 3.</summary>
    Task<IReadOnlyList<Order>> GetAllAsync(string? customerId, CancellationToken cancellationToken = default);

    /// <summary>Stránka objednávek (nejnovější první) – zákazníka, nebo všech; volitelně jen jedno číslo objednávky (hledání).</summary>
    Task<PagedList<Order>> GetPageAsync(string? customerId, int? orderId,
        PageRequest page, CancellationToken cancellationToken = default);
    /// <summary>Počet objednávek a útrata za každého zákazníka (účty krok 6 – přehled uživatelů).</summary>
    Task<IReadOnlyList<CustomerOrderSummaryDto>> GetCustomerSummariesAsync(IReadOnlyCollection<string>? customerIds = null,
        CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 6: objednávky v daných stavech (fronty expedice), nejstarší první.</summary>
    Task<IReadOnlyList<Order>> GetByStatusesAsync(IReadOnlyCollection<OrderStatus> statuses, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 4: objednávka zákazníka z daného odeslání pokladny (idempotence), nebo null.</summary>
    Task<Order?> GetByCheckoutIdAsync(string customerId, Guid checkoutId, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 4: adresa z poslední objednávky kurýrem – předvyplní pokladnu.</summary>
    Task<DeliveryAddress?> GetLastDeliveryAddressAsync(string customerId, CancellationToken cancellationToken = default);
    /// <summary>
    /// E-shop krok 13: použil už zákazník tenhle kupon? Počítají se jen platné objednávky (zamítnuté a zrušené kupon vrátily).
    /// </summary>
    Task<bool> HasUsedCouponAsync(string customerId, int couponId, CancellationToken cancellationToken = default);

    void Add(Order order);
}
