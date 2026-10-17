using MiniShop.Domain.Common;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Reakce na jednu doménovou událost. Jedna událost může mít libovolný počet handlerů (i žádný).
/// Nový handler = nová třída zaregistrovaná v DI, kód, který událost vyvolal, se nemění.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
