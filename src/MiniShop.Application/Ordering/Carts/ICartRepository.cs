using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Application.Ordering.Carts;

/// <summary>E-shop krok 3: košíky v ordering.db – jeden na zákazníka.</summary>
public interface ICartRepository
{
    /// <summary>Košík i s řádky, nebo null (zákazník ještě nic nepřidal).</summary>
    Task<Cart?> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>Počet kusů v košíku – jen součet v SQL, bez načítání řádků (volá se na každé stránce).</summary>
    Task<int> GetTotalQuantityAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 10: košík i s řádky podle ID, nebo null.</summary>
    Task<Cart?> GetByIdAsync(int cartId, CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 10: ID košíků, které nejsou prázdné, od <paramref name="changedBeforeUtc"/> se nezměnily a ještě
    /// nedostaly připomínku. Hledá SQL; konečné rozhodnutí dělá <see cref="Cart.IsAbandoned"/>.
    /// </summary>
    Task<IReadOnlyList<int>> GetAbandonedIdsAsync(DateTime changedBeforeUtc, CancellationToken cancellationToken = default);

    void Add(Cart cart);

    void Remove(Cart cart);
}
