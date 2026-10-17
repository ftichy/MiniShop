using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Infrastructure.Inbox;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence.Configurations;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// Společný základ databáze modulu (krok 10): každý modul má vlastní databázi a v ní vlastní outbox a inbox.
/// Outbox musí ležet ve STEJNÉ databázi jako data modulu – jinak by změna dat a zpráva nebyly v jedné transakci.
/// </summary>
/// <remarks>
/// Potomci: OrderingDbContext (ordering.db) a InventoryDbContext (inventory.db). Každý určí, jaké entity má
/// (<see cref="ConfigureModule"/>) a jak se jeho doménové události zapíšou do outboxu (<see cref="ToOutboxMessage"/>).
/// </remarks>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options), IUnitOfWork
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    /// <summary>Jak doménovou událost modulu zapsat do outboxu (doménová zpráva, nebo rovnou integrační).</summary>
    protected abstract OutboxMessage ToOutboxMessage(IDomainEvent domainEvent);

    /// <summary>Entity modulu (konfigurace EF).</summary>
    protected abstract void ConfigureModule(ModelBuilder modelBuilder);

    /// <summary>
    /// Outbox (krok 4): v JEDNÉ transakci uloží změny entit i řádky outboxu – buď obojí, nebo nic.
    /// Události se tady nerozesílají: doručí je později OutboxProcessor na pozadí (krok 5).
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

        // Když už transakce běží (např. z inboxu, krok 6), přidáme se k ní –
        // potvrdí ji ten, kdo ji začal. Jinak si začneme vlastní.
        await using var ownTransaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Nejdřív entity – nová entita tím dostane ID, které potřebuje zpráva v outboxu.
        var result = await base.SaveChangesAsync(cancellationToken);

        OutboxMessages.AddRange(domainEvents.Select(ToOutboxMessage));
        await base.SaveChangesAsync(cancellationToken);

        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }

        // Vyčistit až po uložení: když selže, události zůstanou u entit.
        entities.ForEach(entity => entity.ClearDomainEvents());

        return result;
    }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedMessageConfiguration());
        ConfigureModule(modelBuilder);
    }
}
