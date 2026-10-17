using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Společný základ consumerů (kroky 8 a 9): založí vlastní frontu, naváže ji na exchange odebíraných kontraktů,
/// přijímá zprávy a potvrzuje je (ack) až PO zpracování. Co se se zprávou udělá, určí potomek v <see cref="HandleAsync"/>.
/// </summary>
/// <remarks>
/// <see cref="RunAsync"/> běží po celou dobu života aplikace (spouští ho RabbitMqConsumersWorker ve Webu).
/// Když broker neběží nebo spojení spadne, za 5 s se připojí znovu. Zprávy mezitím čekají ve frontě.
/// Chyba při čtení (neznámý kontrakt, rozbitý JSON) = zprávu zahodit – opakování nepomůže.
/// Chyba handleru = po pauze vrátit do fronty (retry); duplicitě zabrání inbox.
/// </remarks>
public abstract class RabbitMqQueueConsumer(RabbitMqConnection connection, ILogger logger)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Název vlastní fronty consumera.</summary>
    public abstract string QueueName { get; }

    /// <summary>Kontrakty (= exchange), na které se fronta naváže.</summary>
    protected abstract IReadOnlyCollection<string> ContractNames { get; }

    protected abstract Task HandleAsync(IIntegrationMessage message, string payload, bool redelivered,
        CancellationToken cancellationToken);

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
                    QueueName, ex.Message, ReconnectDelay.TotalSeconds);
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

        // Vlastní fronta consumera + napojení na exchange odebíraných kontraktů (binding).
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        foreach (var contractName in ContractNames)
        {
            await RabbitMqTopology.DeclareExchangeAsync(channel, contractName, cancellationToken);
            await channel.QueueBindAsync(QueueName, contractName, routingKey: string.Empty,
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
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, cancellationToken);
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken);

        logger.LogInformation("Consumer poslouchá frontu {Queue} ({Contracts})", QueueName, string.Join(", ", ContractNames));

        await disconnected.Task.WaitAsync(cancellationToken);
        logger.LogWarning("Spojení s RabbitMQ se přerušilo (fronta {Queue})", QueueName);
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var type = delivery.BasicProperties.Type ?? string.Empty;
        var messageId = delivery.BasicProperties.MessageId;
        var tags = new TagList { { "message.type", type }, { "queue", QueueName } };

        // Krok 11: span zpracování jako potomek odesílatele (hlavička traceparent) – trasa přeskočí frontu.
        // Uvnitř je Activity.Current = tento span, takže zprávy, které handler uloží do outboxu, pokračují ve stejné trase.
        using var activity = MessagingTelemetry.StartProcessActivity(type,
            Guid.TryParse(messageId, out var id) ? id : Guid.Empty,
            RabbitMqMessage.ReadTraceParent(delivery.BasicProperties), QueueName);

        IIntegrationMessage message;
        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(delivery.Body.Span);
            message = IntegrationMessageReader.Read(type, payload, messageId);
        }
        catch (Exception ex)
        {
            activity.RecordError(ex);
            MessagingTelemetry.ConsumerFailed.Add(1, tags);

            // Zprávu nejde přečíst – opakování nepomůže. Bez dead-letter fronty se zahodí.
            logger.LogError(ex, "Zprávu {MessageId} ({Type}) z fronty {Queue} nejde přečíst, zahazuje se",
                messageId, type, QueueName);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        try
        {
            await HandleAsync(message, payload, delivery.Redelivered, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            MessagingTelemetry.ConsumerHandled.Add(1, tags);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            activity.RecordError(ex);
            MessagingTelemetry.ConsumerFailed.Add(1, tags);

            // Chyba handleru (např. zamčená DB) – po pauze vrátit do fronty, broker ji doručí znovu.
            logger.LogWarning("Zprávu {MessageId} ({Type}) z fronty {Queue} se nepodařilo zpracovat, za {Delay} s se vrátí do fronty: {Error}",
                messageId, type, QueueName, RetryDelay.TotalSeconds, ex.Message);
            await Task.Delay(RetryDelay, cancellationToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }
}
