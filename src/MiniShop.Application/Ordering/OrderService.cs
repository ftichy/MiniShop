using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Customers;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Application.Ordering.Saga;
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
    ICartRepository carts,
    IOrderingUnitOfWork unitOfWork,
    IOrderingMessagePublisher publisher,
    IEnumerable<IDiscountPolicy> discountPolicies,
    ReturnSettings returnSettings,
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
        return products.Select(p => p.ToCatalogItemDto()).ToList();
    }

    public Task<IReadOnlyList<CustomerOrderSummaryDto>> GetCustomerSummariesAsync(CancellationToken cancellationToken = default) =>
        orders.GetCustomerSummariesAsync(cancellationToken);

    public async Task<OrderDto> GetByIdAsync(int id, OrderAccess access, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, access, cancellationToken);
        return order.ToDto();
    }

    public async Task<OrderDetailDto> GetDetailAsync(int id, OrderAccess access, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, access, cancellationToken);
        return order.ToDetailDto() with
        {
            // E-shop krok 12: tlačítko Vrátit zboží jen do 14 dní od doručení a jen jednou (rozhoduje Order).
            ReturnableUntilUtc = order.CanRequestReturn(UtcNow, returnSettings.Window) ? order.DeliveredAtUtc + returnSettings.Window : null,
            ReturnReason = order.ReturnReason,
            ReturnRejectionReason = order.ReturnRejectionReason
        };
    }

    public async Task<OrderDto> PlaceOrderAsync(CreateOrderRequest request, OrderCustomer customer,
        CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
        {
            throw new DomainException("Objednávka musí obsahovat alespoň jednu položku.");
        }

        // E-shop krok 4: opakovaný požadavek se stejným checkoutId (výpadek sítě, retry klienta) vrátí hotovou objednávku.
        if (request.CheckoutId is { } checkoutId
            && await orders.GetByCheckoutIdAsync(customer.CustomerId, checkoutId, cancellationToken) is { } placed)
        {
            return placed.ToDto();
        }

        // Stejný produkt zadaný vícekrát sloučíme do jedné položky.
        var requestedItems = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .ToList();

        var productsById = (await catalog.GetAvailableByIdsAsync(requestedItems.Select(i => i.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        // Účty krok 3: zákazník je přihlášený uživatel – jméno a e-mail z jeho účtu, ne z požadavku.
        var order = new Order(customer.CustomerId, customer.Name, customer.Email, UtcNow, request.CheckoutId);

        foreach (var (productId, quantity) in requestedItems)
        {
            if (!productsById.TryGetValue(productId, out var product))
            {
                // Produkt nemusí být v kopii katalogu ještě (nový – zpráva ze skladu nedorazila) nebo už (odebraný).
                throw new NotFoundException($"Produkt s ID {productId} není v katalogu.");
            }

            // Kopie katalogu dodá název a cenu. Zásobu řeší až sklad (sága).
            order.AddItem(product, quantity, discountPolicies.BestDiscount(product, quantity));
        }

        orders.Add(order);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);   // → OrderPlaced do outboxu → sága → ReserveStockV1
        }
        catch (ConcurrencyConflictException) when (request.CheckoutId is not null)
        {
            // Dva souběžné požadavky se stejným checkoutId – druhý narazil na unikátní index; vrátíme ten první.
            var first = await orders.GetByCheckoutIdAsync(customer.CustomerId, request.CheckoutId.Value, cancellationToken);
            if (first is null)
            {
                throw;
            }

            return first.ToDto();
        }

        TraceTags.TagOrder(order.Id);                            // ID přidělila až databáze

        return order.ToDto();
    }

    public async Task<IReadOnlyList<OrderDto>> GetFulfilmentQueueAsync(CancellationToken cancellationToken = default)
    {
        var inProgress = await orders.GetByStatusesAsync(
            [OrderStatus.Confirmed, OrderStatus.Packing, OrderStatus.Shipped], cancellationToken);
        return inProgress.Select(o => o.ToDto()).ToList();
    }

    public Task<OrderDto> StartPackingAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, order => order.StartPacking(UtcNow), cancellationToken);                         // → OrderPackingStarted

    public Task<OrderDto> ShipAsync(int id, string? carrier, string? trackingNumber, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, order => order.Ship(carrier, trackingNumber, UtcNow), cancellationToken);        // → OrderShipped → OrderShippedV1

    /// <remarks>E-shop krok 6: „vyřídit“ = doručeno – jde až po odeslání (dřív rovnou z Confirmed).</remarks>
    public Task<OrderDto> CompleteAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, order => order.MarkDelivered(UtcNow), cancellationToken);                        // → OrderCompleted

    /// <summary>
    /// Expedice (jen správce – hlídá Web): načíst, změnit stav, uložit. Souběžná změna téže objednávky (zákazník zrušil)
    /// skončí při uložení <see cref="ConcurrencyConflictException"/> – nic se nepřepíše.
    /// </summary>
    private async Task<OrderDto> ChangeAsync(int id, Action<Order> change, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, OrderAccess.AllOrders, cancellationToken);
        change(order);

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

    public async Task<OrderDto> RequestReturnAsync(int id, OrderAccess access, string reason,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, access, cancellationToken);
        order.RequestReturn(reason, UtcNow, returnSettings.Window);   // vyvolá OrderReturnRequested → sága vratky

        // Dál (převzetí zboží, naskladnění, peníze) řídí ReturnSagaOrchestrator; e-mail „pošlete zboží“ Notifications.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return order.ToDto();
    }

    /// <summary>E-shop krok 12: objednávky čekající na vrácení (stránka Vratky) – jen admin, hlídá Web.</summary>
    public async Task<IReadOnlyList<OrderDto>> GetReturnQueueAsync(CancellationToken cancellationToken = default) =>
        (await orders.GetByStatusesAsync([OrderStatus.ReturnRequested], cancellationToken)).Select(o => o.ToDto()).ToList();

    public async Task<int> AnonymizeCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var customerOrders = await orders.GetAllAsync(customerId, cancellationToken);

        // Všechno, nebo nic: s rozpracovanou objednávkou se nezmění žádná.
        if (customerOrders.Any(order => order.IsActive))
        {
            throw new DomainException(
                "Účet nejde smazat, dokud máte nevyřízené objednávky. Počkejte na jejich vyřízení, nebo potvrzené zrušte.");
        }

        foreach (var order in customerOrders)
        {
            order.Anonymize();
        }

        // E-shop krok 3: košík je osobní údaj (co si kdo vybral) a bez účtu nemá smysl – pryč celý.
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken);
        if (cart is not null)
        {
            carts.Remove(cart);
        }

        // E-shop krok 7: kdo si z událostí uložil osobní údaje zákazníka (Notifications), musí zapomenout. Ve stejné
        // transakci jako anonymizace (outbox) – buď obojí, nebo nic.
        publisher.Publish(new CustomerAnonymizedV1(Guid.NewGuid(), UtcNow, customerId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return customerOrders.Count;
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

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
