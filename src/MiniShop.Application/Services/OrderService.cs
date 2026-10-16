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
/// Teď je vše synchronní v jedné transakci: objednávka + odečtení skladu + notifikace.
/// V EDA verzi se odečtení skladu a notifikace přesunou do samostatných handlerů události OrderPlaced.
/// </remarks>
public sealed class OrderService(
    IOrderRepository orders,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    IEnumerable<IDiscountPolicy> discountPolicies,
    IOrderNotifier notifier,
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

        var order = new Order(request.CustomerName, request.CustomerEmail, timeProvider.GetUtcNow().UtcDateTime);

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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifier.OrderPlacedAsync(order, cancellationToken);

        return order.ToDto();
    }

    public async Task<OrderDto> CompleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        order.Complete();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifier.OrderCompletedAsync(order, cancellationToken);

        return order.ToDto();
    }

    public async Task<OrderDto> CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        order.Cancel();

        // Vrácení zboží na sklad (produkt mezitím mohl být smazán – ten přeskočíme).
        var productsById = (await products.GetByIdsAsync(order.Items.Select(i => i.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        foreach (var item in order.Items)
        {
            if (productsById.TryGetValue(item.ProductId, out var product))
            {
                product.AddStock(item.Quantity);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifier.OrderCancelledAsync(order, cancellationToken);

        return order.ToDto();
    }

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
