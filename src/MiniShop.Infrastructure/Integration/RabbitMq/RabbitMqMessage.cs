using System.Text.Json;
using MiniShop.Contracts;
using RabbitMQ.Client;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>Jak vypadá integrační zpráva "na drátě": hlavičky (properties) + tělo s JSONem kontraktu.</summary>
public static class RabbitMqMessage
{
    public static BasicProperties CreateProperties(IIntegrationMessage message) => new()
    {
        MessageId = message.MessageId.ToString(),      // příjemce podle něj pozná duplicitu (inbox)
        Type = message.GetType().Name,               // název kontraktu – podle něj příjemce JSON načte
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent,              // zpráva ve frontě přežije restart brokeru
        Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAtUtc).ToUnixTimeSeconds()),
        AppId = "MiniShop"
    };

    public static byte[] Serialize(IIntegrationMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());
}
