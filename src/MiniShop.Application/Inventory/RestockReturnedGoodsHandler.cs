using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Modul Inventory (e-shop krok 12): vyřídí příkaz <see cref="RestockReturnedGoodsV1"/> – vrácené zboží se vrací na sklad.
/// Kolik kusů, ví sklad ze svých <b>vydaných rezervací</b> (jako uvolnění podle aktivních), ne z objednávky.
/// Odpoví <see cref="ReturnedGoodsRestockedV1"/>.
/// </summary>
/// <remarks>
/// Idempotentní i nad rámec inboxu: už vrácené rezervace se přeskočí (<c>CanBeReturned</c>), takže druhý příkaz
/// naskladní 0 kusů a jen znovu odpoví – sága pak může pokračovat. Smazaný produkt se jen označí (není kam naskladnit).
/// </remarks>
public sealed class RestockReturnedGoodsHandler(
    IProductRepository products,
    IStockReservationRepository reservations,
    IInventoryMessagePublisher publisher,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IIntegrationMessageHandler<RestockReturnedGoodsV1>
{
    public async Task HandleAsync(RestockReturnedGoodsV1 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var returnable = await reservations.GetReturnableByOrderIdAsync(command.OrderId, cancellationToken);
        var productsById = (await products.GetByIdsAsync(returnable.Select(r => r.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        var quantity = 0;
        foreach (var reservation in returnable)
        {
            if (productsById.TryGetValue(reservation.ProductId, out var product))
            {
                product.AddStock(reservation.Quantity, now);   // → změna dostupnosti → výloha (StockAvailabilityChangedV1)
                quantity += reservation.Quantity;
            }

            reservation.MarkReturned(now);
        }

        publisher.Publish(new ReturnedGoodsRestockedV1(Guid.NewGuid(), now, command.OrderId, quantity));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
