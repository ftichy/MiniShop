using MiniShop.Contracts;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Testovací náhrada brokeru – zapamatuje si, co odešlo. Volitelně zprávu předá dál skutečnému busu
/// (např. InProcessIntegrationBus), aby proběhla celá sága.
/// </summary>
public sealed class RecordingIntegrationBus(IIntegrationBus? inner = null) : IIntegrationBus
{
    public List<IIntegrationMessage> Published { get; } = new();

    /// <summary>Když je nastavená, PublishAsync ji vyhodí – simuluje nedostupný broker nebo jinou chybu.</summary>
    public Exception? FailWith { get; set; }

    public async Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        if (inner is not null)
        {
            await inner.PublishAsync(message, cancellationToken);
        }

        Published.Add(message);
    }
}
