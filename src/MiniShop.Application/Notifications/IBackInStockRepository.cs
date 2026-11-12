using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>Upozornění na naskladnění (modul Notifications).</summary>
public interface IBackInStockRepository
{
    Task<BackInStockSubscription?> FindAsync(string customerId, int productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackInStockSubscription>> ListForProductAsync(int productId, CancellationToken cancellationToken = default);

    void Add(BackInStockSubscription subscription);

    void Remove(BackInStockSubscription subscription);

    /// <summary>Zapomenout zákazníka (smazal účet).</summary>
    Task<int> RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}
