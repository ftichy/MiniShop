using MiniShop.Contracts;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Tests.TestDoubles;

/// <summary>Testovací náhrada brokeru – jen si zapamatuje, co by odešlo ven.</summary>
public sealed class RecordingIntegrationEventBus : IIntegrationEventBus
{
    public List<IIntegrationEvent> Published { get; } = new();

    /// <summary>Když je nastavená, PublishAsync ji vyhodí – simuluje nedostupný broker nebo jinou chybu.</summary>
    public Exception? FailWith { get; set; }

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
