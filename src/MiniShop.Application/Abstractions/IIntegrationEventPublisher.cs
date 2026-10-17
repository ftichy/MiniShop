using MiniShop.Contracts;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Publikuje integrační událost pro jiné služby (krok 7).
/// </summary>
/// <remarks>
/// Zpráva se neodesílá hned: zapíše se do outboxu a uloží až s nejbližším <see cref="IUnitOfWork.SaveChangesAsync"/>,
/// tedy ve stejné transakci jako ostatní změny. Ven ji pak pošle worker outboxu.
/// </remarks>
public interface IIntegrationEventPublisher
{
    void Publish(IIntegrationEvent integrationEvent);
}
