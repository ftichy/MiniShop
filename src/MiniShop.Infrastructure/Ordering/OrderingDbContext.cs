using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Carts;
using MiniShop.Domain.Orders.Coupons;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Infrastructure.Ordering.Configurations;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// Databáze modulu Ordering – ordering.db (krok 10): objednávky, ságy, kopie katalogu + vlastní outbox a inbox.
/// Tabulky skladu tu nejsou – Ordering je nevidí.
/// </summary>
/// <remarks>
/// Doménové události objednávek jdou do outboxu jako doménové zprávy; worker je pak doručí handlerům
/// a překladový handler z nich vyrobí integrační (krok 7).
/// </remarks>
public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : ModuleDbContext(options), IOrderingUnitOfWork
{
    /// <summary>Škálování krok 1: schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "ordering";

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderSaga> OrderSagas => Set<OrderSaga>();

    /// <summary>E-shop krok 12: ságy vrácení zboží.</summary>
    public DbSet<ReturnSaga> ReturnSagas => Set<ReturnSaga>();

    /// <summary>E-shop krok 13: slevové kódy.</summary>
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();
    public DbSet<Cart> Carts => Set<Cart>();

    /// <summary>E-shop krok 17: události košíků (event store) – zdroj pravdy košíku, <c>Carts</c> je projekce.</summary>
    public DbSet<CartEventRecord> CartEvents => Set<CartEventRecord>();

    /// <summary>
    /// E-shop krok 17: nové události sledovaných košíků se přidají do <c>CartEvents</c> a uloží se ve stejné transakci
    /// jako projekce košíku (a případně objednávka a outbox). Stejný vzor jako outbox: data a záznam o změně naráz.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var carts = ChangeTracker.Entries<Cart>()
            .Where(entry => entry.State != EntityState.Deleted && entry.Entity.UncommittedEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();
        var records = carts
            .SelectMany(cart => cart.UncommittedEvents.Select((cartEvent, i) => CartEventSerializer.ToRecord(cart, cart.Version + i + 1, cartEvent)))
            .ToList();
        CartEvents.AddRange(records);

        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            carts.ForEach(cart => cart.MarkEventsCommitted());
            return result;
        }
        catch
        {
            // Neuložilo se (souběh…): záznamy zahodit, ať je další pokus ve stejném scope nepřidá podruhé.
            records.ForEach(record => Entry(record).State = EntityState.Detached);
            throw;
        }
    }

    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) =>
        [OrderingDomainEvents.ToOutboxMessage(domainEvent)];

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly,
            type => type.Namespace == typeof(OrderConfiguration).Namespace);
}
