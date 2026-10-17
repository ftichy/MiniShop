using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// Projekce (krok 10): z událostí skladu udržuje v Orderingu vlastní kopii katalogu – <see cref="CatalogProduct"/>.
/// Ordering pak při objednávce nemusí sahat do databáze skladu (database per service).
/// </summary>
/// <remarks>
/// Zprávy můžou přijít dvakrát (inbox) i v jiném pořadí (retry) – o pořadí se stará CatalogProduct podle OccurredAtUtc.
/// </remarks>
public sealed class CatalogProjectionHandler(ICatalogProductRepository catalog, IOrderingUnitOfWork unitOfWork) :
    IIntegrationMessageHandler<ProductUpdatedV1>,
    IIntegrationMessageHandler<ProductRemovedV1>
{
    public async Task HandleAsync(ProductUpdatedV1 message, CancellationToken cancellationToken)
    {
        var product = await catalog.GetByIdAsync(message.ProductId, cancellationToken);
        if (product is null)
        {
            catalog.Add(new CatalogProduct(message.ProductId, message.Name, message.Price, message.OccurredAtUtc));
        }
        else
        {
            product.Apply(message.Name, message.Price, message.OccurredAtUtc);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(ProductRemovedV1 message, CancellationToken cancellationToken)
    {
        var product = await catalog.GetByIdAsync(message.ProductId, cancellationToken);
        if (product is null)
        {
            return;   // o produktu jsme se nedozvěděli – není co odebírat
        }

        product.MarkRemoved(message.OccurredAtUtc);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
