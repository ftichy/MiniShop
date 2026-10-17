using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Infrastructure.Inbox;

namespace MiniShop.Infrastructure.Events;

/// <summary>
/// In-process dispatcher: pro každou událost najde v DI všechny IDomainEventHandler&lt;TEvent&gt; a zavolá je.
/// </summary>
/// <remarks>
/// Chyba v jednom handleru se zaloguje a ostatní handlery běží dál – data už jsou v tuto chvíli uložená.
/// Na konci se selhání nahlásí výjimkou, aby outbox (krok 4) nechal událost jako nezpracovanou.
/// </remarks>
public sealed class DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    // Pro každý typ události si jednou vytvoříme "invoker", který umí zavolat generické handlery bez reflexe.
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> Invokers = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        var failures = new List<Exception>();

        foreach (var domainEvent in domainEvents)
        {
            var invoker = Invokers.GetOrAdd(
                domainEvent.GetType(),
                eventType => (HandlerInvoker)Activator.CreateInstance(typeof(HandlerInvoker<>).MakeGenericType(eventType))!);

            await invoker.InvokeAsync(serviceProvider, domainEvent, logger, failures, cancellationToken);
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Některé handlery doménových událostí selhaly.", failures);
        }
    }

    internal abstract class HandlerInvoker
    {
        public abstract Task InvokeAsync(
            IServiceProvider services, IDomainEvent domainEvent, ILogger logger, List<Exception> failures, CancellationToken cancellationToken);
    }

    internal sealed class HandlerInvoker<TEvent> : HandlerInvoker where TEvent : IDomainEvent
    {
        public override async Task InvokeAsync(
            IServiceProvider services, IDomainEvent domainEvent, ILogger logger, List<Exception> failures, CancellationToken cancellationToken)
        {
            foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            {
                try
                {
                    await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    // U dekorátoru (krok 6) logujeme skutečný handler, ne obal.
                    var handlerType = (handler as IDomainEventHandlerDecorator)?.InnerHandlerType ?? handler.GetType();
                    logger.LogError(ex, "Handler {Handler} selhal při zpracování události {Event}",
                        handlerType.Name, typeof(TEvent).Name);
                    failures.Add(ex);
                }
            }
        }
    }
}
