using System.Text;
using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Ukázkový příjemce (krok 8): poslouchá frontu "minishop.audit" navázanou na všechny integrační události,
/// každou zprávu zaloguje a potvrdí (ack). Ukazuje druhou stranu brokeru – tak bude vypadat sklad nebo notifikace.
/// </summary>
/// <remarks>
/// <see cref="RunAsync"/> běží po celou dobu života aplikace (volá ho IntegrationEventAuditWorker ve Webu).
/// Když broker neběží nebo spojení spadne, za 5 s se připojí znovu. Zprávy mezitím čekají ve frontě.
/// Ack až PO zpracování: když consumer spadne uprostřed, broker zprávu doručí znovu (Redelivered = true).
/// </remarks>
public sealed class RabbitMqAuditConsumer(
    RabbitMqConnection connection,
    RabbitMqOptions options,
    ILogger<RabbitMqAuditConsumer> logger)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilDisconnectedAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Consumer fronty {Queue} nemá spojení s RabbitMQ: {Error}. Další pokus za {Delay} s.",
                    options.AuditQueue, ex.Message, ReconnectDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConsumeUntilDisconnectedAsync(CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);

        // Vlastní fronta consumeru + napojení na exchange všech kontraktů (binding).
        await channel.QueueDeclareAsync(options.AuditQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        foreach (var contractName in IntegrationEventReader.ContractNames)
        {
            await RabbitMqTopology.DeclareExchangeAsync(channel, contractName, cancellationToken);
            await channel.QueueBindAsync(options.AuditQueue, contractName, routingKey: string.Empty,
                cancellationToken: cancellationToken);
        }

        // Broker pošle najednou nejvýš 10 nepotvrzených zpráv (prefetch).
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, cancellationToken);
        await channel.BasicConsumeAsync(options.AuditQueue, autoAck: false, consumer, cancellationToken);

        logger.LogInformation("Consumer poslouchá frontu {Queue}", options.AuditQueue);

        await disconnected.Task.WaitAsync(cancellationToken);
        logger.LogWarning("Spojení s RabbitMQ se přerušilo (fronta {Queue})", options.AuditQueue);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var type = delivery.BasicProperties.Type ?? string.Empty;
        try
        {
            var payload = Encoding.UTF8.GetString(delivery.Body.Span);
            var integrationEvent = IntegrationEventReader.Read(type, payload, delivery.BasicProperties.MessageId);

            logger.LogInformation("[Přijato z {Queue}] {Type} {EventId}{Redelivered}: {Payload}",
                options.AuditQueue, type, integrationEvent.EventId, delivery.Redelivered ? " (opakované doručení)" : "", payload);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Zprávu nejde přečíst (neznámý kontrakt, rozbitý JSON) – opakování nepomůže. Bez dead-letter fronty se zahodí.
            logger.LogError(ex, "Zprávu {MessageId} ({Type}) z fronty {Queue} se nepodařilo zpracovat, zahazuje se",
                delivery.BasicProperties.MessageId, type, options.AuditQueue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
        }
    }
}
