using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Bus bez brokeru (Messaging:Transport = "InProcess"): zprávu zaloguje a rovnou ji doručí handlerům modulů
/// v tomtéž procesu. Sága tak funguje i bez Dockeru a běží na něm testy.
/// </summary>
/// <remarks>
/// Každý modul dostane zprávu ve vlastním scope (vlastní DbContext) – jako by měl vlastní frontu.
/// Když některý modul selže, vyhodí se chyba a outbox zprávu zkusí znovu; moduly, které už uspěly, přeskočí inbox.
/// </remarks>
public sealed class InProcessIntegrationBus(
    IServiceScopeFactory scopeFactory,
    IntegrationMessageRouting routing,
    ILogger<InProcessIntegrationBus> logger) : IIntegrationBus
{
    public async Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default)
    {
        var modules = routing.ModulesFor(message.GetType());

        logger.LogInformation("[Integrační zpráva] {Type} {MessageId} → {Modules}: {Payload}",
            message.GetType().Name,
            message.MessageId,
            modules.Count == 0 ? "nikdo neodebírá" : string.Join(", ", modules),
            JsonSerializer.Serialize(message, message.GetType()));

        foreach (var module in modules)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IntegrationMessageDispatcher>()
                .DispatchAsync(message, module, cancellationToken);
        }
    }
}
