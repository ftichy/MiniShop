using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>Rezervace skladu (krok 9, modul Inventory).</summary>
public interface IStockReservationRepository
{
    /// <summary>Platné (neuvolněné) rezervace objednávky.</summary>
    Task<IReadOnlyList<StockReservation>> GetActiveByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);

    void Add(StockReservation reservation);
}
