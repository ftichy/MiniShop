using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Jedno TCP spojení s RabbitMQ pro celou aplikaci (singleton). Spojení je drahé, kanály (channel) levné –
/// publisher i consumer si nad ním otevírají vlastní kanály.
/// </summary>
/// <remarks>
/// Když spojení spadne (broker restartoval), při dalším požadavku se vytvoří nové. Automatickou obnovu
/// klienta (AutomaticRecoveryEnabled) vypínáme – o opakování se starají outbox worker a consumer samy.
/// </remarks>
public sealed class RabbitMqConnection(RabbitMqOptions options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    /// <param name="publisherConfirms">
    /// true = BasicPublishAsync počká, až broker potvrdí převzetí zprávy (publisher confirms).
    /// </param>
    public async Task<IChannel> CreateChannelAsync(bool publisherConfirms, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: publisherConfirms,
                publisherConfirmationTrackingEnabled: publisherConfirms),
            cancellationToken);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName = options.HostName,
                Port = options.Port,
                UserName = options.UserName,
                Password = options.Password,
                ClientProvidedName = "MiniShop",   // jméno spojení v management UI
                AutomaticRecoveryEnabled = false,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Připojeno k RabbitMQ {Host}:{Port}", options.HostName, options.Port);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
