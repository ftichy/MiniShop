using MiniShop.Contracts;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Odesílání integračních zpráv jiným službám: událostí (krok 7) i příkazů (krok 9).
/// </summary>
/// <remarks>
/// Zpráva se neodesílá hned: zapíše se do outboxu a uloží až s nejbližším <see cref="IUnitOfWork.SaveChangesAsync"/>,
/// tedy ve stejné transakci jako ostatní změny. Ven ji pak pošle worker outboxu.
/// </remarks>
public interface IIntegrationMessagePublisher
{
    /// <summary>Událost "stalo se" – dostane ji každý, kdo ji odebírá (i nikdo).</summary>
    void Publish(IIntegrationEvent integrationEvent);

    /// <summary>Příkaz "udělej" – dostane ho právě jeden příjemce, služba, která ho vlastní.</summary>
    void Send(IIntegrationCommand command);
}
