using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
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
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderSaga> OrderSagas => Set<OrderSaga>();
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();

    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) =>
        [OrderingDomainEvents.ToOutboxMessage(domainEvent)];

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly,
            type => type.Namespace == typeof(OrderConfiguration).Namespace);
}
