using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Inventory;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Common;
using MiniShop.Domain.Inventory;
using MiniShop.Domain.Inventory.Events;
using MiniShop.Infrastructure.Inventory.Configurations;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Inventory;

/// <summary>
/// Databáze modulu Inventory – inventory.db (krok 10): produkty, rezervace + vlastní outbox a inbox.
/// Objednávky tu nejsou – Inventory o nich ví jen to, co přijde v příkazech.
/// </summary>
/// <remarks>
/// Doménové události produktu se při uložení rovnou převedou na INTEGRAČNÍ zprávy (ProductChanged → ProductUpdatedV1 + V2,
/// StockAvailabilityChanged → StockAvailabilityChangedV1).
/// Je to druhá varianta překladu z kroku 7: Ordering překládá v handleru (o kolo workeru později),
/// Inventory přímo při SaveChanges (hned, v téže transakci). Inventory totiž žádné vlastní doménové handlery nemá.
/// </remarks>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : ModuleDbContext(options), IInventoryUnitOfWork
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public DbSet<Category> Categories => Set<Category>();

    // EventId integrační zprávy = EventId doménové události – při opakovaném uložení stejné události se nezmění.
    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) => domainEvent switch
    {
        // E-shop krok 2: stejná změna ve dvou verzích kontraktu (expand → contract). V2 má odvozené ID – jiné než V1,
        // ale při opakovaném uložení pořád stejné.
        ProductChanged e =>
        [
            OutboxMessageFactory.Create(
                new ProductUpdatedV1(e.EventId, e.OccurredAtUtc, e.Product.Id, e.Product.Name, e.Product.Price)),
            OutboxMessageFactory.Create(
                e.Product.ToProductUpdatedV2(MessageIds.Derive(e.EventId, nameof(ProductUpdatedV2)), e.OccurredAtUtc))
        ],
        StockAvailabilityChanged e =>
        [
            OutboxMessageFactory.Create(
                new StockAvailabilityChangedV1(e.EventId, e.OccurredAtUtc, e.Product.Id, e.Availability.ToContract()))
        ],
        ProductRemoved e => [OutboxMessageFactory.Create(new ProductRemovedV1(e.EventId, e.OccurredAtUtc, e.ProductId))],
        _ => throw new InvalidOperationException(
            $"Událost {domainEvent.GetType().Name} nemá převod do outboxu. Doplňte ji do {nameof(InventoryDbContext)}.")
    };

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly,
            type => type.Namespace == typeof(ProductConfiguration).Namespace);
}
