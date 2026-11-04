using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Reviews;
using MiniShop.Domain.Reviews;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Infrastructure.Reviews;

public sealed class ReviewRepository(ReviewsDbContext db) : IReviewRepository
{
    public async Task<IReadOnlyList<Review>> GetForProductAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.Reviews.Where(r => r.ProductId == productId).ToListAsync(cancellationToken);

    public Task<Review?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.Reviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Review>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        await db.Reviews.AsNoTracking().OrderByDescending(r => r.Id).Take(count).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Review>> GetByCustomerAsync(string customerId, CancellationToken cancellationToken = default) =>
        await db.Reviews.Where(r => r.CustomerId == customerId).ToListAsync(cancellationToken);

    public void Add(Review review) => db.Reviews.Add(review);
}

public sealed class PurchaseRepository(ReviewsDbContext db) : IPurchaseRepository
{
    public Task<PurchasedOrder?> GetAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.PurchasedOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

    public Task<PurchasedOrder?> FindReviewableAsync(string customerId, int productId, CancellationToken cancellationToken = default) =>
        db.PurchasedOrders
            .Where(o => o.CustomerId == customerId && o.IsDelivered && !o.IsReturned && o.Products.Any(p => p.ProductId == productId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => db.PurchasedOrders.AnyAsync(cancellationToken);

    public async Task RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default) =>
        db.PurchasedOrders.RemoveRange(await db.PurchasedOrders.Where(o => o.CustomerId == customerId).ToListAsync(cancellationToken));

    public void Add(PurchasedOrder order) => db.PurchasedOrders.Add(order);
}

/// <summary>E-shop krok 14: zprávy z modulu Reviews jdou do outboxu v reviews.db.</summary>
public sealed class ReviewsMessagePublisher(ReviewsDbContext db)
    : OutboxIntegrationMessagePublisher(db), IReviewsMessagePublisher;
