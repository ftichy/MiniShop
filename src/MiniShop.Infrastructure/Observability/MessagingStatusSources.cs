using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Observability;

/// <summary>
/// Observabilita G2: stav outboxu všech modulů této služby – čekající a poison zprávy (stejná čísla jako stránka
/// <c>/Admin/Messages</c>, zdroj <see cref="IMessageStore"/>). Štítek <c>module</c>.
/// </summary>
public sealed class OutboxStatusSource(IEnumerable<IMessageStore> stores) : IOperationalMetricsSource
{
    public string Name => "outbox";

    public async Task<IReadOnlyList<OperationalReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var readings = new List<OperationalReading>();
        foreach (var store in stores)
        {
            var status = await store.GetStatusAsync(cancellationToken);
            var module = new KeyValuePair<string, object?>("module", status.Module);
            readings.Add(new(OperationalMetrics.OutboxPending, status.Pending, module));
            readings.Add(new(OperationalMetrics.OutboxPoison, status.PoisonCount, module));
        }

        return readings;
    }
}

/// <summary>
/// Observabilita G2: fronty modulů této služby v RabbitMQ – kolik zpráv čeká na consumer (backlog) a kolik leží
/// v dead-letter frontě (stejný zdroj jako health check <c>dead-letters</c>). Štítek <c>queue</c> (např. <c>minishop.ordering</c>).
/// </summary>
public sealed class RabbitMqQueueStatusSource(RabbitMqDeadLetterService queues) : IOperationalMetricsSource
{
    public string Name => "rabbitmq";

    public async Task<IReadOnlyList<OperationalReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var readings = new List<OperationalReading>();
        foreach (var (queue, count) in await queues.GetBacklogCountsAsync(cancellationToken))
        {
            readings.Add(new(OperationalMetrics.QueueBacklog, count, new KeyValuePair<string, object?>("queue", queue)));
        }

        foreach (var (queue, count) in await queues.GetCountsAsync(cancellationToken))
        {
            readings.Add(new(OperationalMetrics.QueueDeadLetters, count, new KeyValuePair<string, object?>("queue", queue)));
        }

        return readings;
    }
}
