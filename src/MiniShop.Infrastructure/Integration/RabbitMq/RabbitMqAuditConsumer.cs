using Microsoft.Extensions.Logging;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Ukázkový příjemce (krok 8): poslouchá frontu "minishop.audit" navázanou na VŠECHNY kontrakty (události i příkazy)
/// a každou zprávu jen zaloguje. Je to "odposlech" (wire tap) – v logu je vidět celý průběh ságy.
/// </summary>
public sealed class RabbitMqAuditConsumer(
    RabbitMqConnection connection,
    RabbitMqOptions options,
    ILogger<RabbitMqAuditConsumer> logger)
    : RabbitMqQueueConsumer(connection, logger)
{
    public override string QueueName => options.AuditQueue;

    protected override IReadOnlyCollection<string> ContractNames => IntegrationMessageReader.ContractNames;

    protected override Task HandleAsync(IIntegrationMessage message, string payload, bool redelivered,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("[Přijato z {Queue}] {Type} {MessageId}{Redelivered}: {Payload}",
            QueueName, message.GetType().Name, message.MessageId, redelivered ? " (opakované doručení)" : "", payload);
        return Task.CompletedTask;
    }
}
