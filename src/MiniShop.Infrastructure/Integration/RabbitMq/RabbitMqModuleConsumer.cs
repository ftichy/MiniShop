using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Consumer jednoho modulu (krok 9): fronta "minishop.{modul}" navázaná jen na kontrakty, které modul odebírá
/// (např. minishop.inventory ← ReserveStockV1, ReleaseStockV1). Zprávy předá handlerům modulu přes <see cref="IntegrationMessageDispatcher"/>.
/// </summary>
/// <remarks>
/// Vlastní fronta = modul dostane svou kopii zprávy, i když ho jiný modul už zpracoval, a zprávy na něj počkají,
/// když neběží. V kroku 10 se z modulu stane samostatná služba a jen si tuto frontu "odnese".
/// </remarks>
public sealed class RabbitMqModuleConsumer(
    string module,
    RabbitMqConnection connection,
    IntegrationMessageRouting routing,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqModuleConsumer> logger)
    : RabbitMqQueueConsumer(connection, logger)
{
    public override string QueueName => $"minishop.{module}";

    protected override IReadOnlyCollection<string> ContractNames => routing.ContractNamesFor(module);

    protected override async Task HandleAsync(IIntegrationMessage message, string payload, bool redelivered,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntegrationMessageDispatcher>()
            .DispatchAsync(message, module, cancellationToken);
    }
}

/// <summary>Vyrobí všechny consumery: auditní + jeden pro každý modul, který něco odebírá.</summary>
public sealed class RabbitMqConsumers(IServiceProvider services, IntegrationMessageRouting routing)
{
    public IReadOnlyList<RabbitMqQueueConsumer> CreateAll() =>
    [
        services.GetRequiredService<RabbitMqAuditConsumer>(),
        .. routing.Modules.Select(module => ActivatorUtilities.CreateInstance<RabbitMqModuleConsumer>(services, module))
    ];
}
