using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using MiniShop.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Odeslání integrační události do RabbitMQ (krok 8). Nahrazuje LoggingIntegrationEventBus – zbytek aplikace se nemění.
/// </summary>
/// <remarks>
/// Kanál má zapnuté publisher confirms: PublishAsync skončí, až broker potvrdí, že zprávu převzal.
/// Teprve potom worker označí řádek v outboxu jako zpracovaný – zpráva se tak nemůže ztratit cestou.
/// Když broker neběží, vyhodí <see cref="IntegrationBusUnavailableException"/> a zpráva v outboxu počká.
/// Volá ho jen worker outboxu (jedno vlákno), zámek je pojistka – kanál nesmí publikovat ze dvou vláken naráz.
/// </remarks>
public sealed class RabbitMqIntegrationEventBus(RabbitMqConnection connection, ILogger<RabbitMqIntegrationEventBus> logger)
    : IIntegrationEventBus, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _declaredExchanges = new();
    private IChannel? _channel;

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var exchange = integrationEvent.GetType().Name;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);

            if (!_declaredExchanges.Contains(exchange))
            {
                await RabbitMqTopology.DeclareExchangeAsync(channel, exchange, cancellationToken);
                _declaredExchanges.Add(exchange);
            }

            // Fanout ignoruje routing key. Když na exchange není navázaná žádná fronta, broker zprávu zahodí –
            // u publish/subscribe to tak má být: nikdo neposlouchá = nikdo nedostane.
            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: string.Empty,
                mandatory: false,
                basicProperties: RabbitMqMessage.CreateProperties(integrationEvent),
                body: RabbitMqMessage.Serialize(integrationEvent),
                cancellationToken: cancellationToken);

            logger.LogInformation("[RabbitMQ] {Type} {EventId} publikována do exchange {Exchange}",
                exchange, integrationEvent.EventId, exchange);
        }
        catch (Exception ex) when (IsBrokerUnavailable(ex))
        {
            await ResetChannelAsync();
            throw new IntegrationBusUnavailableException($"RabbitMQ není dostupný: {ex.Message}", ex);
        }
        catch
        {
            // Např. broker zprávu odmítl (nack) nebo zavřel kanál kvůli chybě – příště začneme s čistým kanálem.
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await ResetChannelAsync();
        _channel = await connection.CreateChannelAsync(publisherConfirms: true, cancellationToken);
        return _channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        // Broker mohl mezitím přijít o exchange (např. nový kontejner) – po obnovení spojení je deklarujeme znovu.
        _declaredExchanges.Clear();
    }

    private static bool IsBrokerUnavailable(Exception ex) =>
        ex is BrokerUnreachableException or ConnectFailureException or AlreadyClosedException
            or SocketException or IOException;

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _lock.Dispose();
    }
}
