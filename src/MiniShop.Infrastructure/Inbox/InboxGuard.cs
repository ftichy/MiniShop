using Microsoft.EntityFrameworkCore;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Inbox;

/// <summary>
/// "Sešit odškrtnutých zásilek" (krok 6): spustí akci handleru jen jednou pro danou dvojici (ID zprávy, handler).
/// Používá ho dekorátor doménových handlerů i dispatcher integračních zpráv (krok 9).
/// Inbox leží v databázi modulu, jehož handler běží (krok 10) – jinak by nebyl v téže transakci jako jeho změny.
/// </summary>
/// <remarks>
/// Změny handleru a záznam v inboxu se potvrdí v JEDNÉ transakci – buď obojí, nebo nic.
/// Vnější akce (e-mail) v transakci nejsou: když aplikace spadne po odeslání, ale před zápisem do inboxu,
/// odejde e-mail při opakování znovu.
/// </remarks>
public sealed class InboxGuard(ModuleDbContext db, TimeProvider timeProvider)
{
    public async Task RunOnceAsync(Guid messageId, string handlerName, Func<Task> handle, CancellationToken cancellationToken)
    {
        // PŘED: tuhle zásilku už mám odškrtnutou?
        if (await db.ProcessedMessages.AnyAsync(
                m => m.EventId == messageId && m.Handler == handlerName, cancellationToken))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Handler může sám volat SaveChangesAsync – uloží se do téže (zatím nepotvrzené) transakce.
            await handle();

            // PO: odškrtnout. Změny handleru a záznam v inboxu se potvrdí spolu.
            db.ProcessedMessages.Add(new ProcessedMessage
            {
                EventId = messageId,
                Handler = handlerName,
                ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Transakce se vrátí při Dispose. Zahodit i rozpracované změny ve change trackeru,
            // jinak by je uložil až další handler téže zprávy (sdílí stejný DbContext).
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
