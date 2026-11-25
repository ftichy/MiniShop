using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Modul Inventory (krok 9): vyřídí příkaz <see cref="ReserveStockV1"/> – zarezervuje všechno, nebo nic.
/// Odpoví událostí <see cref="StockReservedV1"/> (+ od e-shop kroku 28B <see cref="StockReservedV2"/> s balíky) / <see cref="StockReservationFailedV1"/>.
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
        TraceTags.TagOrder(command.OrderId);   // span "process ReserveStockV1" ve skladu
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Pro tuhle objednávku už rezervace je (stejný příkaz s jiným ID) – jen odpovědět znovu.
        var existing = await reservations.GetActiveByOrderIdAsync(command.OrderId, cancellationToken);
        if (existing.Count > 0)
        {
            await PublishReservedAsync(command.OrderId, [.. existing.Select(r => new StockAllocation(r.WarehouseId, r.ProductId, r.Quantity))],
                now, cancellationToken);
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

        TraceTags.Tag("minishop.stock.lines", TraceTags.Lines(lines));   // na trasu: produkt×kusy, výsledek rezervace
        TraceTags.Tag("minishop.stock.result", problems.Count > 0 ? "failed" : "reserved");
        if (problems.Count > 0)
        {
            publisher.Publish(new StockReservationFailedV1(Guid.NewGuid(), now, command.OrderId,
                "Nedostatek zboží – " + string.Join("; ", problems)));
        }
        else
        {
            // E-shop krok 28A: odkud se zboží vezme (jeden sklad, když to jde) – rozhoduje sklad.
            var allocations = StockAllocator.Allocate([.. lines.Select(l => (productsById[l.ProductId], l.Quantity))])
                ?? throw new InvalidOperationException("Přidělení selhalo, přestože celková zásoba stačí.");
            foreach (var allocation in allocations)
            {
                // e-shop krok 2: změna pásma → StockAvailabilityChangedV1
                productsById[allocation.ProductId].RemoveStock(allocation.WarehouseId, allocation.Quantity, now);
                reservations.Add(new StockReservation(command.OrderId, allocation.ProductId, allocation.Quantity, now, allocation.WarehouseId));
            }

            TraceTags.Tag("minishop.stock.warehouses", allocations.Select(a => a.WarehouseId).Distinct().Count());
            await PublishReservedAsync(command.OrderId, allocations, now, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Odpověď „zarezervováno“: V1 pro ságu a od e-shop kroku 28B i V2 s balíky – jeden za každý sklad (expand → contract).
    /// Sklad bez názvu v DB (nemělo by nastat) se pošle pod kódem.
    /// </summary>
    private async Task PublishReservedAsync(int orderId, IReadOnlyList<StockAllocation> allocations, DateTime now,
        CancellationToken cancellationToken)
    {
        var warehouses = (await products.GetWarehousesAsync(cancellationToken)).ToDictionary(w => w.Id);
        var parcels = allocations
            .GroupBy(a => a.WarehouseId)
            .OrderBy(g => g.Key)
            .Select(g => new StockParcelV1(
                warehouses.TryGetValue(g.Key, out var w) ? w.Code : g.Key.ToString(),
                warehouses.TryGetValue(g.Key, out var named) ? named.Name : g.Key.ToString(),
                [.. g.GroupBy(a => a.ProductId).Select(p => new StockLineV1(p.Key, p.Sum(a => a.Quantity)))]))
            .ToList();

        publisher.Publish(new StockReservedV1(Guid.NewGuid(), now, orderId));
        publisher.Publish(new StockReservedV2(Guid.NewGuid(), now, orderId, parcels));
    }
}
