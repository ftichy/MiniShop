using System.Text.Json;
using MiniShop.Contracts;
using MiniShop.Domain.Common;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Vyrobí řádek outboxu z doménové události (krok 4) nebo z integrační zprávy (kroky 7 a 9).
/// </summary>
/// <remarks>
/// Doménová událost nese entitu, která do JSONu nepatří – jaká data z ní uložit (payload), určuje modul
/// (např. OrderingDomainEvents). Integrační zpráva je už sama o sobě jen data – payload je přímo její JSON.
/// </remarks>
public static class OutboxMessageFactory
{
    /// <param name="payload">Jen data události (ID, e-mail, položky) – žádné entity.</param>
    public static OutboxMessage Create(IDomainEvent domainEvent, object payload) => new()
    {
        Id = domainEvent.EventId,   // ID řádku = ID události, aby ho worker mohl vrátit do znovu sestavené události
        Kind = OutboxMessageKind.Domain,
        Type = domainEvent.GetType().Name,
        Payload = JsonSerializer.Serialize(payload, payload.GetType()),
        OccurredAtUtc = domainEvent.OccurredAtUtc
    };

    /// <summary>
    /// Type je název kontraktu včetně verze ("OrderPlacedV1"), podle něj zprávu <see cref="IntegrationMessageReader"/> načte zpět.
    /// </summary>
    public static OutboxMessage Create(IIntegrationMessage message) => new()
    {
        Id = message.MessageId,
        Kind = OutboxMessageKind.Integration,
        Type = message.GetType().Name,
        Payload = JsonSerializer.Serialize(message, message.GetType()),
        OccurredAtUtc = message.OccurredAtUtc
    };
}
