using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Publikování události i odeslání příkazu = přidání řádku do outboxu (kroky 7 a 9).
/// Uloží se až s nejbližším SaveChangesAsync, ve stejné transakci jako ostatní změny. Ven ji pošle worker.
/// </summary>
/// <remarks>
/// Od kroku 10 má každý modul vlastní outbox – potomci (OrderingMessagePublisher, InventoryMessagePublisher)
/// jen určí, do které databáze se zapisuje.
/// </remarks>
public abstract class OutboxIntegrationMessagePublisher(ModuleDbContext db) : IIntegrationMessagePublisher
{
    public void Publish(IIntegrationEvent integrationEvent) =>
        db.OutboxMessages.Add(OutboxMessageFactory.Create(integrationEvent));

    public void Send(IIntegrationCommand command) =>
        db.OutboxMessages.Add(OutboxMessageFactory.Create(command));
}
