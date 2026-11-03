using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>Rezervace skladu (krok 9, modul Inventory).</summary>
public interface IStockReservationRepository
{
    /// <summary>Platné rezervace objednávky (neuvolněné a od e-shop kroku 6 ani nevydané).</summary>
    Task<IReadOnlyList<StockReservation>> GetActiveByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 12: vydané a ještě nevrácené rezervace objednávky (podklad pro naskladnění vratky).</summary>
    Task<IReadOnlyList<StockReservation>> GetReturnableByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);

    void Add(StockReservation reservation);
}
