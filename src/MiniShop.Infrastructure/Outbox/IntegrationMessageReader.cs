using System.Text.Json;
using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Z názvu kontraktu ("OrderPlacedV1", "ReserveStockV1") a JSONu znovu sestaví integrační zprávu (kroky 7 a 9).
/// Typ najde mezi všemi třídami v MiniShop.Contracts. Používá ho worker outboxu i consumery z RabbitMQ (krok 8).
/// </summary>
/// <remarks>
/// Na rozdíl od <see cref="OutboxEventReader"/> nic nenačítá z DB – integrační zpráva nese všechna data sama.
/// System.Text.Json pole, která kontrakt nezná, ignoruje – to je "tolerant reader".
/// </remarks>
public static class IntegrationMessageReader
{
    private static readonly IReadOnlyDictionary<string, Type> ContractTypes = typeof(IIntegrationMessage).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationMessage).IsAssignableFrom(t))
        .ToDictionary(t => t.Name);

    /// <summary>Názvy všech známých kontraktů – podle nich se v RabbitMQ zakládají exchange.</summary>
    public static IReadOnlyCollection<string> ContractNames => ContractTypes.Keys.ToList();

    public static IIntegrationMessage Read(OutboxMessage message) => Read(message.Type, message.Payload, message.Id.ToString());

    /// <param name="messageId">Jen pro chybovou hlášku.</param>
    public static IIntegrationMessage Read(string type, string payload, string? messageId = null)
    {
        if (!ContractTypes.TryGetValue(type, out var contractType))
        {
            throw new InvalidOperationException($"Neznámý kontrakt integrační zprávy: {type}.");
        }

        return (IIntegrationMessage?)JsonSerializer.Deserialize(payload, contractType)
            ?? throw new InvalidOperationException($"Zpráva {messageId} má prázdný payload.");
    }
}
