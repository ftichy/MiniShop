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
/// Chyba při čtení (neznámý kontrakt, rozbitý JSON) = rovnou do dead-letter fronty (EDA krok 13) – opakování nepomůže.
/// Chyba handleru = po rostoucí pauze znovu (nejvýš 5×, pak dead-letter fronta); duplicitě zabrání inbox.
/// </remarks>
public abstract class RabbitMqQueueConsumer(RabbitMqConnection connection, ILogger logger)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

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
        await RabbitMqDeadLetters.DeclareDeadQueueAsync(channel, QueueName, cancellationToken);   // EDA krok 13
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

            // Zprávu nejde přečíst – opakování nepomůže. EDA krok 13: neztratí se, jde rovnou do dead-letter fronty.
            logger.LogError(ex, "Zprávu {MessageId} ({Type}) z fronty {Queue} nejde přečíst, přesouvá se do {DeadQueue}",
                messageId, type, QueueName, RabbitMqDeadLetters.DeadQueueFor(QueueName));
            await DeadLetterAsync(channel, delivery, RabbitMqDeadLetters.ReadAttempts(delivery.BasicProperties), ex.Message,
                cancellationToken);
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
            var attempts = RabbitMqDeadLetters.ReadAttempts(delivery.BasicProperties) + 1;

            if (attempts >= RabbitMqDeadLetters.MaxDeliveryAttempts)
            {
                // EDA krok 13: dost pokusů – zpráva jde stranou (dead-letter), ať neběží v kruhu. Admin ji vrátí ze stránky Zprávy.
                logger.LogError("Zpráva {MessageId} ({Type}) z fronty {Queue} selhala {Attempts}×, přesouvá se do {DeadQueue}: {Error}",
                    messageId, type, QueueName, attempts, RabbitMqDeadLetters.DeadQueueFor(QueueName), ex.Message);
                await DeadLetterAsync(channel, delivery, attempts, ex.Message, cancellationToken);
                return;
            }

            // Chyba handleru (např. zamčená DB) – po rostoucí pauze kopie zpět do fronty s počtem pokusů, originál potvrdit.
            // (Spadne-li služba mezi publikováním a potvrzením, přijde zpráva dvakrát – zachytí to inbox.)
            var delay = RabbitMqDeadLetters.RetryDelay(attempts);
            logger.LogWarning("Zprávu {MessageId} ({Type}) z fronty {Queue} se nepodařilo zpracovat ({Attempts}/{Max}), za {Delay} s znovu: {Error}",
                messageId, type, QueueName, attempts, RabbitMqDeadLetters.MaxDeliveryAttempts, delay.TotalSeconds, ex.Message);
            await Task.Delay(delay, cancellationToken);
            await channel.BasicPublishAsync(string.Empty, QueueName, mandatory: false,
                RabbitMqDeadLetters.CopyProperties(delivery.BasicProperties, attempts), delivery.Body, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
    }

    /// <summary>EDA krok 13: zprávu do <c>{fronta}.dead</c> (s chybou v hlavičce) a originál potvrdit.</summary>
    private async Task DeadLetterAsync(IChannel channel, BasicDeliverEventArgs delivery, int attempts, string error,
        CancellationToken cancellationToken)
    {
        await channel.BasicPublishAsync(string.Empty, RabbitMqDeadLetters.DeadQueueFor(QueueName), mandatory: false,
            RabbitMqDeadLetters.CopyProperties(delivery.BasicProperties, attempts, error), delivery.Body, cancellationToken);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
    }
}
