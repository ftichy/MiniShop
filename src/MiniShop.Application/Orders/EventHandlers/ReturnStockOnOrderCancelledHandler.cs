using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Orders.EventHandlers;

/// <summary>
/// Po zrušení objednávky vrátí zboží z jejích položek zpět na sklad.
/// </summary>
/// <remarks>
/// Běží až PO uložení zrušené objednávky a ukládá ve vlastním, druhém SaveChangesAsync.
/// Když tady něco selže, objednávka zůstane zrušená, ale zboží se nevrátí (dispatcher chybu jen zaloguje).
/// Tento "dual-write" problém vyřeší outbox v kroku 4.
/// </remarks>
public sealed class ReturnStockOnOrderCancelledHandler(IProductRepository products, IUnitOfWork unitOfWork)
    : IDomainEventHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken)
    {
        var items = domainEvent.Order.Items;

        // Produkt mezitím mohl být smazán – ten přeskočíme.
        var productsById = (await products.GetByIdsAsync(items.Select(i => i.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        foreach (var item in items)
        {
            if (productsById.TryGetValue(item.ProductId, out var product))
            {
                product.AddStock(item.Quantity);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
