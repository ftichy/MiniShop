using MiniShop.Contracts;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Tests.TestDoubles;

/// <summary>Testovací náhrada brokeru – jen si zapamatuje, co by odešlo ven.</summary>
public sealed class RecordingIntegrationEventBus : IIntegrationEventBus
{
    public List<IIntegrationEvent> Published { get; } = new();

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
