using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Modul Inventory (e-shop krok 12): vyřídí příkaz <see cref="RestockReturnedGoodsV1"/> – vrácené zboží se vrací na sklad.
/// Kolik kusů, ví sklad ze svých <b>vydaných rezervací</b> (jako uvolnění podle aktivních), ne z objednávky.
/// Odpoví <see cref="ReturnedGoodsRestockedV1"/>.
/// E-shop krok 22: <see cref="RestockReturnedGoodsV2"/> = jedna vratka s vybranými kusy (k objednávce jich může být víc)
/// → <see cref="ReturnedGoodsRestockedV2"/>.
/// </summary>
/// <remarks>
/// Idempotentní i nad rámec inboxu: V1 přeskočí už vrácené rezervace, V2 si pamatuje naskladněné vratky
/// (<see cref="StockReturn"/>, unikátní objednávka + pořadí) – opakovaný příkaz nic nenaskladní, jen znovu odpoví,
/// aby sága mohla pokračovat. Smazaný produkt se jen označí (není kam naskladnit).
/// </remarks>
public sealed class RestockReturnedGoodsHandler(
    IProductRepository products,
    IStockReservationRepository reservations,
    IInventoryMessagePublisher publisher,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<RestockReturnedGoodsV1>,
    IIntegrationMessageHandler<RestockReturnedGoodsV2>
{
    public async Task HandleAsync(RestockReturnedGoodsV1 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var returnable = await reservations.GetReturnableByOrderIdAsync(command.OrderId, cancellationToken);
        var productsById = await ProductsAsync(returnable, cancellationToken);

        var quantity = 0;
        foreach (var reservation in returnable)
        {
            var returned = reservation.MarkReturned(now);   // zbytek po případných částečných vratkách
            if (productsById.TryGetValue(reservation.ProductId, out var product))
            {
                product.AddStock(returned, now);   // → změna dostupnosti → výloha (StockAvailabilityChangedV1)
                quantity += returned;
            }
        }

        publisher.Publish(new ReturnedGoodsRestockedV1(Guid.NewGuid(), now, command.OrderId, quantity));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TraceTags.Tag("minishop.stock.result", "returned");
        TraceTags.Tag("minishop.stock.quantity", quantity);
    }

    /// <summary>
    /// E-shop krok 22: naskladnit jen kusy jedné vratky. Kusy se berou z vydaných rezervací daného produktu; když jich
    /// je méně, než příkaz chce (nekonzistence), naskladní se jen to, co rezervace dovolí – odpověď nese skutečný počet.
    /// </summary>
    public async Task HandleAsync(RestockReturnedGoodsV2 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);
        TraceTags.Tag("minishop.return.number", command.ReturnNumber);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (await reservations.GetReturnAsync(command.OrderId, command.ReturnNumber, cancellationToken) is { } done)
        {
            // Už naskladněno (opakovaný příkaz) – jen znovu odpovědět.
            publisher.Publish(new ReturnedGoodsRestockedV2(Guid.NewGuid(), now, command.OrderId, command.ReturnNumber, done.Quantity));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var returnable = await reservations.GetReturnableByOrderIdAsync(command.OrderId, cancellationToken);
        var productsById = await ProductsAsync(returnable, cancellationToken);

        var quantity = 0;
        var missing = 0;
        foreach (var line in command.Lines)
        {
            var remaining = line.Quantity;
            foreach (var reservation in returnable.Where(r => r.ProductId == line.ProductId && r.CanBeReturned))
            {
                var part = Math.Min(remaining, reservation.ReturnableQuantity);
                if (part == 0)
                {
                    break;
                }

                reservation.Return(part, now);
                if (productsById.TryGetValue(reservation.ProductId, out var product))
                {
                    product.AddStock(part, now);   // → změna dostupnosti → výloha
                    quantity += part;
                }

                remaining -= part;
            }

            missing += remaining;
        }

        reservations.AddReturn(new StockReturn(command.OrderId, command.ReturnNumber, quantity, now));
        publisher.Publish(new ReturnedGoodsRestockedV2(Guid.NewGuid(), now, command.OrderId, command.ReturnNumber, quantity));
        await unitOfWork.SaveChangesAsync(cancellationToken);   // souběh téže vratky = unikátní index → opakování skončí výš
        TraceTags.Tag("minishop.stock.result", "returned");
        TraceTags.Tag("minishop.stock.quantity", quantity);
        if (missing > 0)
        {
            TraceTags.Tag("minishop.stock.missing", missing);   // vratka chtěla víc kusů, než bylo vydáno – stojí za prověření
        }
    }

    private async Task<Dictionary<int, Product>> ProductsAsync(IReadOnlyList<StockReservation> reservations,
        CancellationToken cancellationToken) =>
        (await products.GetByIdsAsync(reservations.Select(r => r.ProductId).Distinct(), cancellationToken)).ToDictionary(p => p.Id);
}
