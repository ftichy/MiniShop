using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Publikování integrační události = přidání řádku do outboxu (krok 7).
/// Uloží se až s nejbližším SaveChangesAsync, ve stejné transakci jako ostatní změny. Ven ji pošle worker.
/// </summary>
public sealed class OutboxIntegrationEventPublisher(MiniShopDbContext db) : IIntegrationEventPublisher
{
    public void Publish(IIntegrationEvent integrationEvent) =>
        db.OutboxMessages.Add(OutboxMessageFactory.Create(integrationEvent));
}
