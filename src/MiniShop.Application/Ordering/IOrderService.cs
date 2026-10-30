namespace MiniShop.Application.Ordering;

/// <remarks>
/// Účty krok 3: čtení a zrušení dostávají <see cref="OrderAccess"/> – zákazník vidí a ruší jen své objednávky,
/// cizí se tváří jako neexistující (NotFoundException → 404). Vyřízení je jen pro správce (hlídá Web).
/// </remarks>
public interface IOrderService
{
    Task<IReadOnlyList<OrderDto>> GetAllAsync(OrderAccess access, CancellationToken cancellationToken = default);
    /// <summary>Krok 10B: nabídka z vlastní kopie katalogu – funguje, i když služba skladu neběží.</summary>
    Task<IReadOnlyList<CatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Účty krok 6: počet objednávek a útrata za zákazníka – jen pro admina (hlídá Web).</summary>
    Task<IReadOnlyList<CustomerOrderSummaryDto>> GetCustomerSummariesAsync(CancellationToken cancellationToken = default);

    Task<OrderDto> GetByIdAsync(int id, OrderAccess access, CancellationToken cancellationToken = default);
    Task<OrderDto> PlaceOrderAsync(CreateOrderRequest request, OrderCustomer customer, CancellationToken cancellationToken = default);
    Task<OrderDto> CompleteAsync(int id, CancellationToken cancellationToken = default);
    Task<OrderDto> CancelAsync(int id, OrderAccess access, CancellationToken cancellationToken = default);

    /// <summary>
    /// Účty krok 9: zákazník maže účet – jeho objednávky přijdou o jméno a e-mail (zůstanou položky a ceny).
    /// S nevyřízenou objednávkou (Pending, Confirmed) DomainException a nic se nezmění. Vrací počet objednávek.
    /// Volá jen Web před smazáním účtu (opakovat jde bezpečně).
    /// </summary>
    Task<int> AnonymizeCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}
