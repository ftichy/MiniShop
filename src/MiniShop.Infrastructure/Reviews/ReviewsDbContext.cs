using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Reviews;
using MiniShop.Domain.Common;
using MiniShop.Domain.Reviews;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Reviews.Configurations;

namespace MiniShop.Infrastructure.Reviews;

/// <summary>
/// E-shop krok 14: databáze modulu Reviews – reviews.db: hodnocení, kopie nákupů (kdo co dostal), inbox a outbox
/// (souhrn hodnocení <c>ProductRatingChangedV1</c>, žádost o nákupy <c>SyncPurchasesV1</c>).
/// </summary>
public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : ModuleDbContext(options), IReviewsUnitOfWork
{
    /// <summary>Škálování krok 1: schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "reviews";

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<PurchasedOrder> PurchasedOrders => Set<PurchasedOrder>();

    // Reviews nemá doménové události – zprávy posílá služba rovnou přes publisher.
    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) =>
        throw new InvalidOperationException($"Modul Reviews nemá doménové události ({domainEvent.GetType().Name}).");

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewsDbContext).Assembly,
            type => type.Namespace == typeof(ReviewConfiguration).Namespace);
}
