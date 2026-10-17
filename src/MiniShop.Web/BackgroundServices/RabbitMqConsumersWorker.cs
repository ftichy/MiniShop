using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// Proces na pozadí (kroky 8 a 9): spustí všechny consumery RabbitMQ – auditní frontu a fronty modulů
/// (minishop.ordering, minishop.inventory). Každý běží souběžně a sám se po výpadku připojí znovu.
/// Logika je v <see cref="RabbitMqQueueConsumer"/>, tady jen hosting – stejné rozdělení jako OutboxWorker / OutboxProcessor.
/// </summary>
public sealed class RabbitMqConsumersWorker(RabbitMqConsumers consumers) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nebrzdit start aplikace.
        await Task.Yield();

        await Task.WhenAll(consumers.CreateAll().Select(consumer => consumer.RunAsync(stoppingToken)));
    }
}
