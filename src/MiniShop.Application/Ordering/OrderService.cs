using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

/// <summary>
/// Use-casy objednávek. Všechny závislosti jsou abstrakce předané konstruktorem (Dependency Inversion).
/// </summary>
/// <remarks>
/// Od kroku 3 řeší jen objednávku samotnou. Vedlejší účinky (notifikace, vrácení skladu při zrušení)
/// obstarávají handlery doménových událostí ve složce Ordering/EventHandlers.
/// Od kroku 5 běží handlery asynchronně: SaveChangesAsync uloží událost do outboxu a worker ji
/// doručí o chvilku později (eventual consistency).
/// Od kroku 9 sklad vůbec nemění: objednávka vznikne jako Pending a o rezervaci se postará sága
/// (OrderSagaOrchestrator → příkaz ReserveStockV1 → modul Inventory). Nedostatek zboží proto není chyba 400,
/// ale zamítnutá objednávka.
/// Od kroku 10 nečte tabulku produktů skladu, ale vlastní kopii katalogu (CatalogProduct) – název a cenu.
/// </remarks>
public sealed class OrderService(
    IOrderRepository orders,
    ICatalogProductRepository catalog,
    IOrderingUnitOfWork unitOfWork,
    IEnumerable<IDiscountPolicy> discountPolicies,
    TimeProvider timeProvider) : IOrderService
{
    public async Task<IReadOnlyList<OrderDto>> GetAllAsync(OrderAccess access, CancellationToken cancellationToken = default)
    {
        // Účty krok 3: filtr přímo v dotazu do databáze – cizí objednávky se vůbec nenačtou.
        var visible = await orders.GetAllAsync(access.CustomerId, cancellationToken);
        return visible.Select(o => o.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<CatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var products = await catalog.GetAvailableAsync(cancellationToken);
        return products.Select(p => new CatalogItemDto(p.Id, p.Name, p.Price)).ToList();
    }

    public async Task<OrderDto> GetByIdAsync(int id, OrderAccess access, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, access, cancellationToken);
        return order.ToDto();
    }

    public async Task<OrderDto> PlaceOrderAsync(CreateOrderRequest request, OrderCustomer customer,
        CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
        {
            throw new DomainException("Objednávka musí obsahovat alespoň jednu položku.");
        }

        // Stejný produkt zadaný vícekrát sloučíme do jedné položky.
        var requestedItems = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .ToList();

        var productsById = (await catalog.GetAvailableByIdsAsync(requestedItems.Select(i => i.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        // Účty krok 3: zákazník je přihlášený uživatel – jméno a e-mail z jeho účtu, ne z požadavku.
        var order = new Order(customer.CustomerId, customer.Name, customer.Email, UtcNow);

        foreach (var (productId, quantity) in requestedItems)
        {
            if (!productsById.TryGetValue(productId, out var product))
            {
                // Produkt nemusí být v kopii katalogu ještě (nový – zpráva ze skladu nedorazila) nebo už (odebraný).
                throw new NotFoundException($"Produkt s ID {productId} není v katalogu.");
            }

            // Kopie katalogu dodá název a cenu. Zásobu řeší až sklad (sága).
            order.AddItem(product, quantity, CalculateDiscount(product, quantity));
        }

        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → OrderPlaced do outboxu → sága → ReserveStockV1
        TraceTags.TagOrder(order.Id);                            // ID přidělila až databáze

        return order.ToDto();
    }

    public async Task<OrderDto> CompleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, OrderAccess.AllOrders, cancellationToken);   // jen správce (hlídá Web)
        order.Complete(UtcNow);                                  // vyvolá OrderCompleted

        await unitOfWork.SaveChangesAsync(cancellationToken);   // → outbox → později handlery

        return order.ToDto();
    }

    public async Task<OrderDto> CancelAsync(int id, OrderAccess access, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, access, cancellationToken);
        order.Cancel(UtcNow);                                    // vyvolá OrderCancelled

        // Vrácení zboží zajistí sága (příkaz ReleaseStockV1 pro sklad), oznámení handler OrderCancelled.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return order.ToDto();
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    // Uplatní se nejvýhodnější sleva ze všech pravidel (slevy se nesčítají).
    private decimal CalculateDiscount(CatalogProduct product, int quantity) =>
        discountPolicies
            .Select(policy => policy.CalculateDiscount(product, quantity))
            .DefaultIfEmpty(0m)
            .Max();

    /// <remarks>
    /// Účty krok 3: cizí objednávka se tváří jako neexistující (stejná výjimka, stejná hláška → 404) –
    /// zákazník tak ani nezjistí, že objednávka s tím číslem existuje.
    /// </remarks>
    private async Task<Order> GetOrderAsync(int id, OrderAccess access, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(id);   // krok 11: trasu požadavku (detail, vyřízení, zrušení) půjde najít podle čísla objednávky
        var order = await orders.GetByIdAsync(id, cancellationToken);
        return order is not null && access.CanSee(order)
            ? order
            : throw new NotFoundException($"Objednávka s ID {id} nebyla nalezena.");
    }
}
