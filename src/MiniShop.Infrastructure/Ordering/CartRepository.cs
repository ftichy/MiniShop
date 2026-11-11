using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// E-shop krok 3: košíky v modulu Ordering. Krok 17: košík se načte z projekce (<c>Carts</c> + <c>CartLines</c>)
/// a stav si přehraje ze svých událostí (<see cref="Cart.Rehydrate"/>) – zdrojem pravdy je <c>CartEvents</c>.
/// Nové události uloží <see cref="OrderingDbContext"/> ve stejné transakci jako projekci.
/// </summary>
public sealed class CartRepository(OrderingDbContext db) : ICartRepository
{
    public async Task<Cart?> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default) =>
        await RehydrateAsync(await db.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken), cancellationToken);

    // Odznak v hlavičce: rychlý součet z projekce (na každé stránce) – proto projekce existuje.
    public Task<int> GetTotalQuantityAsync(string customerId, CancellationToken cancellationToken = default) =>
        db.Carts
            .Where(c => c.CustomerId == customerId)
            .SelectMany(c => c.Lines)
            .SumAsync(l => l.Quantity, cancellationToken);

    public async Task<Cart?> GetByIdAsync(int cartId, CancellationToken cancellationToken = default) =>
        await RehydrateAsync(await db.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<int>> GetAbandonedIdsAsync(DateTime changedBeforeUtc, CancellationToken cancellationToken = default) =>
        await db.Carts
            .Where(c => c.ReminderSentAtUtc == null && c.UpdatedAtUtc <= changedBeforeUtc && c.Lines.Any())
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

    public void Add(Cart cart) => db.Carts.Add(cart);

    // Smazání účtu: projekce pryč, události smaže databáze (cizí klíč s ON DELETE CASCADE) – celý proud.
    public void Remove(Cart cart) => db.Carts.Remove(cart);

    private async Task<Cart?> RehydrateAsync(Cart? cart, CancellationToken cancellationToken)
    {
        if (cart is not null)
        {
            cart.Rehydrate(await CartEventStore.GetFromLastResetAsync(db, cart.Id, cancellationToken));
        }

        return cart;
    }
}
