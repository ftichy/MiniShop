using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// EF Core kontext. Zároveň slouží jako Unit of Work: ukládá doménové události do outboxu
/// a po uložení je rozesílá handlerům.
/// </summary>
public sealed class MiniShopDbContext : DbContext, IUnitOfWork
{
    private const int MaxErrorLength = 2000;

    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public MiniShopDbContext(
        DbContextOptions<MiniShopDbContext> options,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider)
        : base(options)
    {
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Outbox (krok 4):
    /// 1) v JEDNÉ transakci uloží změny entit i řádky outboxu – buď obojí, nebo nic,
    /// 2) po commitu rozešle události handlerům,
    /// 3) úspěšně doručené řádky označí jako zpracované, u neúspěšných zapíše chybu.
    /// Nezpracovaný řádek v databázi zůstane – událost se tak neztratí ani při chybě handleru nebo pádu aplikace.
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

        int result;
        List<OutboxMessage> messages;

        await using (var transaction = await Database.BeginTransactionAsync(cancellationToken))
        {
            // Nejdřív entity – nová objednávka tím dostane ID, které potřebuje payload v outboxu.
            result = await base.SaveChangesAsync(cancellationToken);

            messages = domainEvents.Select(OutboxMessageFactory.Create).ToList();
            OutboxMessages.AddRange(messages);
            await base.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // Vyčistit až po commitu (když uložení selže, události zůstanou u entit),
        // ale před dispatchem: kdyby handler znovu volal SaveChangesAsync, události se nerozešlou dvakrát.
        entities.ForEach(entity => entity.ClearDomainEvents());

        for (var i = 0; i < domainEvents.Count; i++)
        {
            await DispatchAndMarkAsync(domainEvents[i], messages[i], cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Doručí jednu událost a výsledek zapíše k jejímu řádku v outboxu.
    /// </summary>
    /// <remarks>
    /// Zápis jde přes ExecuteUpdate přímo do DB, mimo change tracker. Obyčejné SaveChanges by
    /// spolu se značkou uložilo i rozpracované změny handleru, který právě selhal.
    /// </remarks>
    private async Task DispatchAndMarkAsync(IDomainEvent domainEvent, OutboxMessage message, CancellationToken cancellationToken)
    {
        // Řádek už je v DB; ve change trackeru by jen zastarával.
        Entry(message).State = EntityState.Detached;

        try
        {
            await _dispatcher.DispatchAsync([domainEvent], cancellationToken);

            var processedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            await OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, processedAtUtc), cancellationToken);
        }
        catch (Exception ex)
        {
            // Handler selhal (dispatcher už chybu zalogoval). Řádek zůstane nezpracovaný a krok 5 ho zkusí znovu.
            var error = ex.Message.Length > MaxErrorLength ? ex.Message[..MaxErrorLength] : ex.Message;
            await OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Error, error), cancellationToken);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiniShopDbContext).Assembly);
    }
}
