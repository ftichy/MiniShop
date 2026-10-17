using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// EF Core kontext. Zároveň slouží jako Unit of Work a po uložení rozesílá doménové události.
/// </summary>
public sealed class MiniShopDbContext : DbContext, IUnitOfWork
{
    private readonly IDomainEventDispatcher _dispatcher;

    public MiniShopDbContext(DbContextOptions<MiniShopDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options)
    {
        _dispatcher = dispatcher;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// 1) posbírá události ze sledovaných entit, 2) uloží změny, 3) teprve potom události rozešle.
    /// Dispatch až po uložení: handlery vidí uložená data i vygenerovaná ID.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entities = ChangeTracker.Entries<Entity>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToList();

        var domainEvents = entities.SelectMany(entity => entity.DomainEvents).ToList();

        // Vyčistit před dispatchem: kdyby handler znovu volal SaveChangesAsync, události se nerozešlou dvakrát.
        entities.ForEach(entity => entity.ClearDomainEvents());

        var result = await base.SaveChangesAsync(cancellationToken);

        await _dispatcher.DispatchAsync(domainEvents, cancellationToken);

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiniShopDbContext).Assembly);
    }
}
