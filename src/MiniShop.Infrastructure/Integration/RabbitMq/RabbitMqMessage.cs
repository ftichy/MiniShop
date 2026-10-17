using System.Text;
using System.Text.Json;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Observability;
using RabbitMQ.Client;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>Jak vypadá integrační zpráva "na drátě": hlavičky (properties) + tělo s JSONem kontraktu.</summary>
public static class RabbitMqMessage
{
    public static BasicProperties CreateProperties(IIntegrationMessage message)
    {
        var properties = new BasicProperties
        {
            MessageId = message.MessageId.ToString(),      // příjemce podle něj pozná duplicitu (inbox)
            Type = message.GetType().Name,               // název kontraktu – podle něj příjemce JSON načte
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,              // zpráva ve frontě přežije restart brokeru
            Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAtUtc).ToUnixTimeSeconds()),
            AppId = "MiniShop"
        };

        // Krok 11: trace ID jde v hlavičce, ne v těle – kontrakt se nemění. Consumer z něj pokračuje v trase.
        if (MessagingTelemetry.CurrentTraceParent() is { } traceParent)
        {
            properties.Headers = new Dictionary<string, object?> { [MessagingTelemetry.TraceParentHeader] = traceParent };
        }

        return properties;
    }

    public static byte[] Serialize(IIntegrationMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());

    /// <summary>Přečte traceparent z hlaviček přijaté zprávy; RabbitMQ.Client vrací textové hlavičky jako byte[].</summary>
    public static string? ReadTraceParent(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(MessagingTelemetry.TraceParentHeader, out var value) == true
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null
            }
            : null;
}
