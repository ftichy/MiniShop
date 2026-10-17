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
    public async Task<IReadOnlyList<OrderDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await orders.GetAllAsync(cancellationToken);
        return all.Select(o => o.ToDto()).ToList();
    }

    public async Task<OrderDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        return order.ToDto();
    }

    public async Task<OrderDto> PlaceOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
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

        var order = new Order(request.CustomerName, request.CustomerEmail, UtcNow);

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

        return order.ToDto();
    }

    public async Task<OrderDto> CompleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        order.Complete(UtcNow);                                  // vyvolá OrderCompleted

        await unitOfWork.SaveChangesAsync(cancellationToken);   // → outbox → později handlery

        return order.ToDto();
    }

    public async Task<OrderDto> CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, cancellationToken);
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

    private async Task<Order> GetOrderAsync(int id, CancellationToken cancellationToken) =>
        await orders.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Objednávka s ID {id} nebyla nalezena.");
}
