using MiniShop.Contracts;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Reakce na integrační zprávu (událost nebo příkaz) od jiné služby (krok 9). Obdoba <see cref="IDomainEventHandler{TEvent}"/>,
/// jen místo doménové události dostane kontrakt z MiniShop.Contracts – nesmí sahat na cizí entity.
/// </summary>
/// <remarks>
/// Handler patří do modulu (Ordering, Inventory). Každý modul má v RabbitMQ vlastní frontu.
/// Registruje se přes AddIntegrationMessageHandler (Infrastructure), který ho obalí inboxem – duplicitu nemusí hlídat sám.
/// </remarks>
public interface IIntegrationMessageHandler<in TMessage> where TMessage : IIntegrationMessage
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken);
}
