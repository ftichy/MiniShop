using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// Proces na pozadí (krok 8): spustí ukázkového consumera fronty minishop.audit v RabbitMQ.
/// Logika (připojení, ack, opakované připojení) je v <see cref="RabbitMqAuditConsumer"/>, tady jen hosting –
/// stejné rozdělení jako OutboxWorker / OutboxProcessor.
/// </summary>
public sealed class IntegrationEventAuditWorker(RabbitMqAuditConsumer consumer) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nebrzdit start aplikace.
        await Task.Yield();

        await consumer.RunAsync(stoppingToken);
    }
}
