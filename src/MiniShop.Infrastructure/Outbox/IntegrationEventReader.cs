using System.Text.Json;
using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Z řádku outboxu druhu Integration znovu sestaví integrační událost (krok 7).
/// Typ najde podle názvu kontraktu ("OrderPlacedV1") mezi všemi třídami v MiniShop.Contracts.
/// </summary>
/// <remarks>
/// Na rozdíl od <see cref="OutboxEventReader"/> nic nenačítá z DB – integrační událost nese všechna data sama.
/// System.Text.Json pole, která kontrakt nezná, ignoruje – to je "tolerant reader".
/// </remarks>
public static class IntegrationEventReader
{
    private static readonly IReadOnlyDictionary<string, Type> ContractTypes = typeof(IIntegrationEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
        .ToDictionary(t => t.Name);

    public static IIntegrationEvent Read(OutboxMessage message)
    {
        if (!ContractTypes.TryGetValue(message.Type, out var type))
        {
            throw new InvalidOperationException($"Neznámý kontrakt integrační události v outboxu: {message.Type}.");
        }

        return (IIntegrationEvent?)JsonSerializer.Deserialize(message.Payload, type)
            ?? throw new InvalidOperationException($"Zpráva {message.Id} má prázdný payload.");
    }
}
