using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Inbox;

/// <summary>
/// Dekorátor (krok 6): obalí handler a zajistí, že každou událost zpracuje nejvýš jednou,
/// i když ji outbox doručí víckrát. Obalený handler o tom neví a nemění se.
/// Samotné odškrtávání je v <see cref="InboxGuard"/> (sdílí ho s handlery integračních zpráv).
/// </summary>
/// <remarks>
/// at-least-once doručení (outbox) + idempotentní příjemce (inbox) = efektivně právě jednou.
/// </remarks>
public sealed class IdempotentDomainEventHandler<TEvent>(
    IDomainEventHandler<TEvent> inner,
    MiniShopDbContext db,
    TimeProvider timeProvider)
    : IDomainEventHandler<TEvent>, IDomainEventHandlerDecorator
    where TEvent : IDomainEvent
{
    private readonly string _handlerName = inner.GetType().FullName ?? inner.GetType().Name;
    private readonly InboxGuard _inbox = new(db, timeProvider);

    public Type InnerHandlerType => inner.GetType();

    public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken) =>
        _inbox.RunOnceAsync(domainEvent.EventId, _handlerName,
            () => inner.HandleAsync(domainEvent, cancellationToken), cancellationToken);
}

/// <summary>Dekorátor handleru – dispatcher z něj zjistí skutečný handler pro logování.</summary>
public interface IDomainEventHandlerDecorator
{
    Type InnerHandlerType { get; }
}
