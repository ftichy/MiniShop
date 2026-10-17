using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Modul Inventory (krok 9): vyřídí příkaz <see cref="ReserveStockV1"/> – zarezervuje všechno, nebo nic.
/// Odpoví událostí <see cref="StockReservedV1"/> / <see cref="StockReservationFailedV1"/>.
/// </summary>
/// <remarks>
/// Nezná objednávky – pracuje jen s daty z příkazu (OrderId, položky). Odečtení skladu, záznam rezervace a odpověď
/// v outboxu se uloží v jedné transakci (spolu s odškrtnutím v inboxu).
/// </remarks>
public sealed class ReserveStockHandler(
    IProductRepository products,
    IStockReservationRepository reservations,
    IInventoryMessagePublisher publisher,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IIntegrationMessageHandler<ReserveStockV1>
{
    public async Task HandleAsync(ReserveStockV1 command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Pro tuhle objednávku už rezervace je (stejný příkaz s jiným ID) – jen odpovědět znovu.
        if ((await reservations.GetActiveByOrderIdAsync(command.OrderId, cancellationToken)).Count > 0)
        {
            publisher.Publish(new StockReservedV1(Guid.NewGuid(), now, command.OrderId));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var lines = command.Lines
            .GroupBy(l => l.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(l => l.Quantity)))
            .ToList();
        var productsById = (await products.GetByIdsAsync(lines.Select(l => l.ProductId), cancellationToken))
            .ToDictionary(p => p.Id);

        // Nejdřív zkontrolovat všechno – rezervuje se buď celá objednávka, nebo nic.
        var problems = lines
            .Select(l => productsById.TryGetValue(l.ProductId, out var p)
                ? p.StockQuantity < l.Quantity ? $"{p.Name}: požadováno {l.Quantity} ks, skladem {p.StockQuantity} ks" : null
                : $"produkt {l.ProductId} neexistuje")
            .OfType<string>()
            .ToList();

        if (problems.Count > 0)
        {
            publisher.Publish(new StockReservationFailedV1(Guid.NewGuid(), now, command.OrderId,
                "Nedostatek zboží – " + string.Join("; ", problems)));
        }
        else
        {
            foreach (var (productId, quantity) in lines)
            {
                productsById[productId].RemoveStock(quantity);
                reservations.Add(new StockReservation(command.OrderId, productId, quantity, now));
            }

            publisher.Publish(new StockReservedV1(Guid.NewGuid(), now, command.OrderId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
