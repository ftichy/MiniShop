using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Inbox;

/// <summary>
/// Dekorátor (krok 6): obalí handler a zajistí, že každou událost zpracuje nejvýš jednou,
/// i když ji outbox doručí víckrát. Obalený handler o tom neví a nemění se.
/// </summary>
/// <remarks>
/// at-least-once doručení (outbox) + idempotentní příjemce (inbox) = efektivně právě jednou.
/// Platí to pro změny v databázi. Vnější akce (e-mail) v transakci nejsou: když aplikace spadne
/// po odeslání, ale před zápisem do inboxu, odejde e-mail při opakování znovu.
/// </remarks>
public sealed class IdempotentDomainEventHandler<TEvent>(
    IDomainEventHandler<TEvent> inner,
    MiniShopDbContext db,
    TimeProvider timeProvider)
    : IDomainEventHandler<TEvent>, IDomainEventHandlerDecorator
    where TEvent : IDomainEvent
{
    private readonly string _handlerName = inner.GetType().FullName ?? inner.GetType().Name;

    public Type InnerHandlerType => inner.GetType();

    public async Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)
    {
        // PŘED: tuhle zásilku už mám odškrtnutou?
        if (await db.ProcessedMessages.AnyAsync(
                m => m.EventId == domainEvent.EventId && m.Handler == _handlerName, cancellationToken))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Handler může sám volat SaveChangesAsync – uloží se do téže (zatím nepotvrzené) transakce.
            await inner.HandleAsync(domainEvent, cancellationToken);

            // PO: odškrtnout. Změny handleru a záznam v inboxu se potvrdí spolu.
            db.ProcessedMessages.Add(new ProcessedMessage
            {
                EventId = domainEvent.EventId,
                Handler = _handlerName,
                ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Transakce se vrátí při Dispose. Zahodit i rozpracované změny ve change trackeru,
            // jinak by je uložil až další handler téže události (sdílí stejný DbContext).
            db.ChangeTracker.Clear();
            throw;
        }
    }
}

/// <summary>Dekorátor handleru – dispatcher z něj zjistí skutečný handler pro logování.</summary>
public interface IDomainEventHandlerDecorator
{
    Type InnerHandlerType { get; }
}
