using Microsoft.Extensions.Hosting;
using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Proces na pozadí (kroky 8 a 9): spustí consumery RabbitMQ – auditní frontu (je-li zapnutá) a fronty modulů,
/// které tahle služba hostuje (Web: minishop.ordering, Inventory.Api: minishop.inventory).
/// Každý běží souběžně a sám se po výpadku připojí znovu.
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
