using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Orders.EventHandlers;

/// <summary>
/// Po zrušení objednávky vrátí zboží z jejích položek zpět na sklad.
/// </summary>
/// <remarks>
/// Běží na pozadí (worker outboxu, krok 5) a ukládá ve vlastním SaveChangesAsync.
/// Když tady něco selže, zpráva v outboxu zůstane nezpracovaná a worker ji zkusí znovu (max. 5×).
/// Že se zboží při opakování nevrátí dvakrát, hlídá dekorátor IdempotentDomainEventHandler (krok 6) –
/// tenhle handler o tom nemusí nic vědět.
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
