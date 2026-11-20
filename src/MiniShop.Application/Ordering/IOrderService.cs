using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Ordering;

/// <remarks>
/// Účty krok 3: čtení a zrušení dostávají <see cref="OrderAccess"/> – zákazník vidí a ruší jen své objednávky,
/// cizí se tváří jako neexistující (NotFoundException → 404). Vyřízení je jen pro správce (hlídá Web).
/// </remarks>
public interface IOrderService
{
    Task<IReadOnlyList<OrderDto>> GetAllAsync(OrderAccess access, CancellationToken cancellationToken = default);

    /// <summary>Stránka objednávek (nejnovější první); <paramref name="orderId"/> = hledání podle čísla (cizí se nenajde).</summary>
    Task<PagedList<OrderDto>> GetPageAsync(OrderAccess access,
        PageRequest page, int? orderId = null, CancellationToken cancellationToken = default);
    /// <summary>Krok 10B: nabídka z vlastní kopie katalogu – funguje, i když služba skladu neběží.</summary>
    Task<IReadOnlyList<CatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Účty krok 6: počet objednávek a útrata za zákazníka – jen pro admina (hlídá Web).</summary>
    Task<IReadOnlyList<CustomerOrderSummaryDto>> GetCustomerSummariesAsync(IReadOnlyCollection<string>? customerIds = null,
        CancellationToken cancellationToken = default);

    Task<OrderDto> GetByIdAsync(int id, OrderAccess access, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 5: detail s historií stavů. Cizí objednávka = NotFoundException jako u <see cref="GetByIdAsync"/>.</summary>
    Task<OrderDetailDto> GetDetailAsync(int id, OrderAccess access, CancellationToken cancellationToken = default);
    Task<OrderDto> PlaceOrderAsync(CreateOrderRequest request, OrderCustomer customer, CancellationToken cancellationToken = default);
    /// <summary>E-shop krok 6: objednávky v expedici (Confirmed, Packing, Shipped) – jen pro admina (hlídá Web).</summary>
    Task<IReadOnlyList<OrderDto>> GetFulfilmentQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 6: začít balit (Confirmed → Packing). Jen admin.</summary>
    /// <remarks>Účty krok 12C: actingUserId = zaměstnanec, který to dělá – vlastní objednávku nevyřizuje (střet zájmů).</remarks>
    Task<OrderDto> StartPackingAsync(int id, string? actingUserId = null, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 6: odeslat (Packing → Shipped); kurýr potřebuje dopravce a číslo zásilky. Jen admin.</summary>
    Task<OrderDto> ShipAsync(int id, string? carrier, string? trackingNumber, string? actingUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 26B: předat zabalenou objednávku (kurýr) dopravci – číslo zásilky přijde zprávou. Jen sklad.</summary>
    Task<OrderDto> RequestShipmentAsync(int id, string? actingUserId = null, CancellationToken cancellationToken = default);

    /// <summary>Doručeno (Shipped → Completed). E-shop krok 6: dřív „vyřídit“ rovnou z Confirmed. Jen admin.</summary>
    Task<OrderDto> CompleteAsync(int id, string? actingUserId = null, CancellationToken cancellationToken = default);
    Task<OrderDto> CancelAsync(int id, OrderAccess access, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 12: zákazník chce doručenou objednávku vrátit (do 14 dní, jednou). Cizí = 404.</summary>
    Task<OrderDto> RequestReturnAsync(int id, OrderAccess access, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 22: zákazník vrací vybrané kusy (produkt → kusy). Vrací číslo vratky; 0 = vratka celé objednávky
    /// (vybral všechno a je to první vratka). Cizí = 404.
    /// </summary>
    Task<int> RequestReturnAsync(int id, OrderAccess access, string reason, IReadOnlyDictionary<int, int> quantities,
        CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 12: objednávky čekající na vrácení (stránka Vratky; krok 22 i s vratkou po částech). Jen admin.</summary>
    Task<IReadOnlyList<OrderDetailDto>> GetReturnQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Účty krok 9: zákazník maže účet – jeho objednávky přijdou o jméno a e-mail (zůstanou položky a ceny).
    /// S nevyřízenou objednávkou (Pending, Confirmed) DomainException a nic se nezmění. Vrací počet objednávek.
    /// Volá jen Web před smazáním účtu (opakovat jde bezpečně). E-shop krok 3: ve stejné transakci smaže i košík.
    /// </summary>
    Task<int> AnonymizeCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}
