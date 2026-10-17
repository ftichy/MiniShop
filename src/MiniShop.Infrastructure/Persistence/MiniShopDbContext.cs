using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// EF Core kontext. Zároveň slouží jako Unit of Work: spolu se změnami entit ukládá doménové události do outboxu.
/// </summary>
public sealed class MiniShopDbContext(DbContextOptions<MiniShopDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Outbox (krok 4): v JEDNÉ transakci uloží změny entit i řádky outboxu – buď obojí, nebo nic.
    /// Události se tady nerozesílají: doručí je později <see cref="OutboxProcessor"/> na pozadí (krok 5).
    /// HTTP požadavek tak skončí hned po uložení a nečeká na handlery.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entities = ChangeTracker.Entries<Entity>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToList();

        if (entities.Count == 0)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        var domainEvents = entities.SelectMany(entity => entity.DomainEvents).ToList();

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

        // Nejdřív entity – nová objednávka tím dostane ID, které potřebuje payload v outboxu.
        var result = await base.SaveChangesAsync(cancellationToken);

        OutboxMessages.AddRange(domainEvents.Select(OutboxMessageFactory.Create));
        await base.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Vyčistit až po commitu: když uložení selže, události zůstanou u entit.
        entities.ForEach(entity => entity.ClearDomainEvents());

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiniShopDbContext).Assembly);
    }
}
