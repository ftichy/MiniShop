using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Reviews;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// Projekce (krok 10): z událostí skladu udržuje v Orderingu vlastní kopii katalogu – <see cref="CatalogProduct"/>.
/// Ordering pak při objednávce nemusí sahat do databáze skladu (database per service).
/// </summary>
/// <remarks>
/// Zprávy můžou přijít dvakrát (inbox) i v jiném pořadí (retry) – o pořadí se stará CatalogProduct podle OccurredAtUtc.
/// E-shop krok 2: sklad posílá každou změnu jako V1 i V2 (expand → contract). Projekce zpracuje obě – V1 kvůli zprávám,
/// které ve frontě čekaly při nasazení, V2 přidá popis, kategorii a dostupnost. Až sklad V1 posílat přestane, handler V1 zmizí.
/// </remarks>
public sealed class CatalogProjectionHandler(ICatalogProductRepository catalog, IOrderingUnitOfWork unitOfWork) :
    IIntegrationMessageHandler<ProductUpdatedV1>,
    IIntegrationMessageHandler<ProductUpdatedV2>,
    IIntegrationMessageHandler<StockAvailabilityChangedV1>,
    IIntegrationMessageHandler<ProductRemovedV1>,
    IIntegrationMessageHandler<ProductRatingChangedV1>
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

    public async Task HandleAsync(ProductUpdatedV2 message, CancellationToken cancellationToken)
    {
        var product = await catalog.GetByIdAsync(message.ProductId, cancellationToken);
        if (product is null)
        {
            product = new CatalogProduct(message.ProductId, message.Name, message.Price, message.OccurredAtUtc);
            catalog.Add(product);
        }

        product.ApplyDetails(message.Name, message.Description, message.Price, message.CategoryId, message.CategoryName,
            message.ImageFileName, message.OccurredAtUtc);

        // Dostupnost má vlastní čas – V2 ji nese jako snímek, novější StockAvailabilityChangedV1 ji nepřepíše zpátky.
        product.ApplyAvailability(ToAvailability(message.Availability), message.AvailabilityChangedAtUtc);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(StockAvailabilityChangedV1 message, CancellationToken cancellationToken)
    {
        var product = await catalog.GetByIdAsync(message.ProductId, cancellationToken);
        if (product is null)
        {
            // Produkt ještě nedorazil – jeho ProductUpdatedV2 přinese dostupnost sám (je v ní snímek celého stavu).
            return;
        }

        product.ApplyAvailability(ToAvailability(message.Availability), message.OccurredAtUtc);
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

    /// <summary>
    /// E-shop krok 14: nový souhrn hodnocení z modulu Reviews – výloha pak ukáže hvězdičky bez dotazu do Reviews.
    /// Produkt, který kopie nezná (nemělo by nastat – hodnotí se jen doručené zboží), se přeskočí.
    /// </summary>
    public async Task HandleAsync(ProductRatingChangedV1 message, CancellationToken cancellationToken)
    {
        if (await catalog.GetByIdAsync(message.ProductId, cancellationToken) is not { } product)
        {
            return;
        }

        product.ApplyRating(message.AverageRating, message.RatingCount, message.OccurredAtUtc);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Text kontraktu → výčet Orderingu. Neznámá hodnota (novější sklad) = Unknown, ne pád.</summary>
    private static CatalogAvailability ToAvailability(string availability) => availability switch
    {
        StockAvailabilityV1.InStock => CatalogAvailability.InStock,
        StockAvailabilityV1.LowStock => CatalogAvailability.LowStock,
        StockAvailabilityV1.OutOfStock => CatalogAvailability.OutOfStock,
        _ => CatalogAvailability.Unknown
    };
}
