using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;

namespace MiniShop.Infrastructure.Inbox;

/// <summary>
/// Dekorátor (krok 6): obalí handler a zajistí, že každou událost zpracuje nejvýš jednou,
/// i když ji outbox doručí víckrát. Obalený handler o tom neví a nemění se.
/// Samotné odškrtávání je v <see cref="InboxGuard"/> (sdílí ho s handlery integračních zpráv).
/// </summary>
/// <remarks>
/// at-least-once doručení (outbox) + idempotentní příjemce (inbox) = efektivně právě jednou.
/// </remarks>
public sealed class IdempotentDomainEventHandler<TEvent>(IDomainEventHandler<TEvent> inner, InboxGuard inbox)
    : IDomainEventHandler<TEvent>, IDomainEventHandlerDecorator
    where TEvent : IDomainEvent
{
    private readonly string _handlerName = inner.GetType().FullName ?? inner.GetType().Name;

    public Type InnerHandlerType => inner.GetType();

    public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken) =>
        inbox.RunOnceAsync(domainEvent.EventId, _handlerName,
            () => inner.HandleAsync(domainEvent, cancellationToken), cancellationToken);
}

/// <summary>Dekorátor handleru – dispatcher z něj zjistí skutečný handler pro logování.</summary>
public interface IDomainEventHandlerDecorator
{
    Type InnerHandlerType { get; }
}
