using System.Text.Json;
using MiniShop.Contracts;
using RabbitMQ.Client;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>Jak vypadá integrační událost "na drátě": hlavičky (properties) + tělo s JSONem kontraktu.</summary>
public static class RabbitMqMessage
{
    public static BasicProperties CreateProperties(IIntegrationEvent integrationEvent) => new()
    {
        MessageId = integrationEvent.EventId.ToString(),      // příjemce podle něj pozná duplicitu (inbox)
        Type = integrationEvent.GetType().Name,               // název kontraktu – podle něj příjemce JSON načte
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent,              // zpráva ve frontě přežije restart brokeru
        Timestamp = new AmqpTimestamp(new DateTimeOffset(integrationEvent.OccurredAtUtc).ToUnixTimeSeconds()),
        AppId = "MiniShop"
    };

    public static byte[] Serialize(IIntegrationEvent integrationEvent) =>
        JsonSerializer.SerializeToUtf8Bytes(integrationEvent, integrationEvent.GetType());
}
