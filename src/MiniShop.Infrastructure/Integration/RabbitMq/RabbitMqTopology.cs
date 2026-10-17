using RabbitMQ.Client;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Topologie = jaké exchange a fronty v RabbitMQ existují a jak jsou propojené.
/// Pro každý kontrakt jedna exchange typu fanout s jeho názvem ("OrderPlacedV1"): každá navázaná fronta dostane kopii.
/// </summary>
/// <remarks>
/// Deklarace je idempotentní – když exchange se stejným nastavením už existuje, nic se nestane.
/// Proto ji klidně deklaruje publisher i consumer, podle toho, kdo se k brokeru připojí dřív.
/// </remarks>
public static class RabbitMqTopology
{
    public static Task DeclareExchangeAsync(IChannel channel, string contractName, CancellationToken cancellationToken = default) =>
        channel.ExchangeDeclareAsync(
            exchange: contractName,
            type: ExchangeType.Fanout,
            durable: true,          // přežije restart brokeru
            autoDelete: false,
            cancellationToken: cancellationToken);
}
