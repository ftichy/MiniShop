using MiniShop.Application.Abstractions;
using MiniShop.Application.Contracts;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Mapping;
using MiniShop.Application.Pricing;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;

namespace MiniShop.Application.Services;

/// <summary>
/// Use-casy objednávek. Všechny závislosti jsou abstrakce předané konstruktorem (Dependency Inversion).
/// </summary>
/// <remarks>
/// Od kroku 3 řeší jen objednávku samotnou. Vedlejší účinky (notifikace, vrácení skladu při zrušení)
/// obstarávají handlery doménových událostí ve složce Orders/EventHandlers.
/// Od kroku 5 běží handlery asynchronně: SaveChangesAsync uloží událost do outboxu a worker ji
/// doručí o chvilku později (eventual consistency).
/// Odečtení skladu při vytvoření zůstává tady: kontrola dostupnosti musí proběhnout PŘED přijetím objednávky
/// (asynchronní rezervaci přinese sága v kroku 9).
/// </remarks>
public sealed class OrderService(
    IOrderRepository orders,
    IProductRepository products,
    IUnitOfWork unitOfWork,
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

        var productsById = (await products.GetByIdsAsync(requestedItems.Select(i => i.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        var order = new Order(request.CustomerName, request.CustomerEmail, UtcNow);

        foreach (var (productId, quantity) in requestedItems)
        {
            if (!productsById.TryGetValue(productId, out var product))
            {
                throw new NotFoundException($"Produkt s ID {productId} nebyl nalezen.");
            }

            product.RemoveStock(quantity);
            order.AddItem(product, quantity, CalculateDiscount(product, quantity));
        }

        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → OrderPlaced do outboxu → později handlery

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

        // Vrácení zboží a oznámení zákazníkovi zajistí handlery OrderCancelled (o chvilku později, přes outbox).
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return order.ToDto();
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    // Uplatní se nejvýhodnější sleva ze všech pravidel (slevy se nesčítají).
    private decimal CalculateDiscount(Product product, int quantity) =>
        discountPolicies
            .Select(policy => policy.CalculateDiscount(product, quantity))
            .DefaultIfEmpty(0m)
            .Max();

    private async Task<Order> GetOrderAsync(int id, CancellationToken cancellationToken) =>
        await orders.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Objednávka s ID {id} nebyla nalezena.");
}
