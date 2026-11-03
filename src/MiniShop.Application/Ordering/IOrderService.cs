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

    /// <summary>E-shop krok 5: detail s historií stavů. Cizí objednávka = NotFoundException jako u <see cref="GetByIdAsync"/>.</summary>
    Task<OrderDetailDto> GetDetailAsync(int id, OrderAccess access, CancellationToken cancellationToken = default);
    Task<OrderDto> PlaceOrderAsync(CreateOrderRequest request, OrderCustomer customer, CancellationToken cancellationToken = default);
    /// <summary>E-shop krok 6: objednávky v expedici (Confirmed, Packing, Shipped) – jen pro admina (hlídá Web).</summary>
    Task<IReadOnlyList<OrderDto>> GetFulfilmentQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 6: začít balit (Confirmed → Packing). Jen admin.</summary>
    Task<OrderDto> StartPackingAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 6: odeslat (Packing → Shipped); kurýr potřebuje dopravce a číslo zásilky. Jen admin.</summary>
    Task<OrderDto> ShipAsync(int id, string? carrier, string? trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>Doručeno (Shipped → Completed). E-shop krok 6: dřív „vyřídit“ rovnou z Confirmed. Jen admin.</summary>
    Task<OrderDto> CompleteAsync(int id, CancellationToken cancellationToken = default);
    Task<OrderDto> CancelAsync(int id, OrderAccess access, CancellationToken cancellationToken = default);

    /// <summary>
    /// Účty krok 9: zákazník maže účet – jeho objednávky přijdou o jméno a e-mail (zůstanou položky a ceny).
    /// S nevyřízenou objednávkou (Pending, Confirmed) DomainException a nic se nezmění. Vrací počet objednávek.
    /// Volá jen Web před smazáním účtu (opakovat jde bezpečně). E-shop krok 3: ve stejné transakci smaže i košík.
    /// </summary>
    Task<int> AnonymizeCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}
