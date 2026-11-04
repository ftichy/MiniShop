using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// EDA krok 13: údržba „pošty“ jednoho modulu – úklid starých záznamů outboxu a inboxu, přehled čekajících a
/// poison zpráv a jejich ruční opakování. Používá ho úklidový worker a stránka Zprávy pro admina.
/// </summary>
/// <remarks>
/// <para>
/// Maže jen <b>zpracované</b> zprávy (ProcessedAtUtc vyplněné) – nezpracované a poison zprávy zůstávají vždy,
/// dokud je nevyřeší člověk.
/// </para>
/// <para>
/// Inbox („tuhle zprávu už jsem zpracoval“) smí zmizet, až když už žádné opakované doručení té zprávy nehrozí.
/// Proto má delší lhůtu než outbox: opakování trvá nejvýš minuty (backoff, fronta), retence je ve dnech.
/// </para>
/// </remarks>
public sealed class MessageStore<TDbContext>(IServiceScopeFactory scopeFactory) : IMessageStore
    where TDbContext : ModuleDbContext
{
    public const int PoisonListLimit = 20;

    public string ModuleName { get; } = typeof(TDbContext).Name.Replace("DbContext", string.Empty);

    public async Task<MessageCleanupResult> CleanUpAsync(DateTime outboxBeforeUtc, DateTime inboxBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var outbox = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < outboxBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
        var inbox = await db.ProcessedMessages
            .Where(m => m.ProcessedAtUtc < inboxBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);

        return new MessageCleanupResult(ModuleName, outbox, inbox);
    }

    public async Task<MessageStoreStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var unprocessed = db.OutboxMessages.AsNoTracking().Where(m => m.ProcessedAtUtc == null);

        var pending = await unprocessed.CountAsync(m => m.Attempts < OutboxProcessor.MaxAttempts, cancellationToken);
        var poison = await unprocessed
            .Where(m => m.Attempts >= OutboxProcessor.MaxAttempts)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(PoisonListLimit)
            .Select(m => new PoisonMessage(m.Id, m.Type, m.OccurredAtUtc, m.Attempts, m.Error))
            .ToListAsync(cancellationToken);
        var poisonCount = await unprocessed.CountAsync(m => m.Attempts >= OutboxProcessor.MaxAttempts, cancellationToken);

        return new MessageStoreStatus(ModuleName, pending, poisonCount, poison);
    }

    /// <summary>Poison zpráva dostane nové pokusy (po opravě příčiny). Vrací false, když taková zpráva není.</summary>
    public async Task<bool> RetryAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        return await db.OutboxMessages
            .Where(m => m.Id == messageId && m.ProcessedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Attempts, 0)
                .SetProperty(m => m.NextAttemptAtUtc, (DateTime?)null), cancellationToken) > 0;
    }
}

/// <summary>EDA krok 13: údržba outboxu a inboxu jednoho modulu (každý modul se zprávami má svou).</summary>
public interface IMessageStore
{
    string ModuleName { get; }

    /// <summary>Smaže zpracované zprávy outboxu starší než hranice a záznamy inboxu starší než druhá hranice.</summary>
    Task<MessageCleanupResult> CleanUpAsync(DateTime outboxBeforeUtc, DateTime inboxBeforeUtc, CancellationToken cancellationToken = default);

    Task<MessageStoreStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> RetryAsync(Guid messageId, CancellationToken cancellationToken = default);
}

public sealed record MessageCleanupResult(string Module, int OutboxDeleted, int InboxDeleted);

/// <param name="Pending">Nezpracované zprávy, které se ještě zkoušejí (i ty čekající na backoff).</param>
/// <param name="PoisonCount">Zprávy, které vyčerpaly pokusy – čekají na člověka.</param>
/// <param name="Poison">Nejstarší poison zprávy (nejvýš <see cref="MessageStore{TDbContext}.PoisonListLimit"/>).</param>
public sealed record MessageStoreStatus(string Module, int Pending, int PoisonCount, IReadOnlyList<PoisonMessage> Poison);

public sealed record PoisonMessage(Guid Id, string Type, DateTime OccurredAtUtc, int Attempts, string? Error);
