using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Common;
using MiniShop.Infrastructure.Inbox;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence.Configurations;
using Npgsql;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// Společný základ databáze modulu (krok 10): každý modul má vlastní databázi a v ní vlastní outbox a inbox.
/// Outbox musí ležet ve STEJNÉ databázi jako data modulu – jinak by změna dat a zpráva nebyly v jedné transakci.
/// </summary>
/// <remarks>
/// Potomci: OrderingDbContext (ordering.db) a InventoryDbContext (inventory.db). Každý určí, jaké entity má
/// (<see cref="ConfigureModule"/>) a jak se jeho doménové události zapíšou do outboxu (<see cref="ToOutboxMessages"/>).
/// </remarks>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options), IUnitOfWork
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    /// <summary>
    /// Jak doménovou událost modulu zapsat do outboxu (doménová zpráva, nebo rovnou integrační). Obvykle jedna zpráva;
    /// víc, když modul posílá stejnou změnu ve dvou verzích kontraktu (e-shop krok 2: ProductUpdatedV1 + V2).
    /// </summary>
    protected abstract IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent);

    /// <summary>Entity modulu (konfigurace EF).</summary>
    protected abstract void ConfigureModule(ModelBuilder modelBuilder);

    /// <summary>
    /// Outbox (krok 4): v JEDNÉ transakci uloží změny entit i řádky outboxu – buď obojí, nebo nic.
    /// Události se tady nerozesílají: doručí je později OutboxProcessor na pozadí (krok 5).
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">
    /// E-shop krok 4: data mezitím změnil jiný požadavek (concurrency token, unikátní index). Aplikační vrstva
    /// o EF Core neví – dostane vlastní výjimku a rozhodne, co dál (pokladna najde hotovou objednávku).
    /// </exception>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SaveWithOutboxAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || IsUniqueViolation(ex))
        {
            throw new ConcurrencyConflictException(ex);
        }
    }

    // Druhý zápis stejné unikátní hodnoty (souběžné odeslání pokladny…): SQLITE_CONSTRAINT_UNIQUE,
    // v PostgreSQL (škálování krok 1) SQLSTATE 23505 unique_violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }
            or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private async Task<int> SaveWithOutboxAsync(CancellationToken cancellationToken)
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

        OutboxMessages.AddRange(domainEvents.SelectMany(ToOutboxMessages));
        await base.SaveChangesAsync(cancellationToken);

        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }

        // Vyčistit až po uložení: když selže, události zůstanou u entit.
        entities.ForEach(entity => entity.ClearDomainEvents());

        return result;
    }

    // Škálování krok 1: v PostgreSQL časy vždy jako UTC (SQLite Kind neukládá).
    protected sealed override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes(this);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseModuleSchema(this);   // škálování krok 1: v PostgreSQL vlastní schéma modulu
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedMessageConfiguration());
        ConfigureModule(modelBuilder);
    }
}
