using Microsoft.Extensions.Logging;
using MiniShop.Contracts;
using RabbitMQ.Client;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Vzhled krok F3a: komu se má zpráva <b>rozeslat</b> (každé kopii služby), ne rozdělit. Dostane ji každá kopie – např. Web
/// pošle připojeným prohlížečům signál „obnov se“ (SignalR). Implementace je ve službě (Web: <c>LiveUpdatesNotifier</c>).
/// </summary>
public interface IBroadcastMessageHandler
{
    Task HandleAsync(IIntegrationMessage message, CancellationToken cancellationToken);
}

/// <summary>Kontrakty, které se kopiím rozesílají (= exchange, na které se dočasná fronta naváže).</summary>
public sealed record BroadcastSubscription(IReadOnlyCollection<string> ContractNames);

/// <summary>
/// Vzhled krok F3a: <b>rozeslání každé kopii</b> (fanout na dočasné fronty). Běžná fronta modulu zprávy mezi kopie
/// <b>rozdělí</b> (každou zpracuje jedna); tahle fronta patří jen této kopii (<c>exclusive</c>, zanikne s jejím spojením)
/// a je navázaná na tytéž exchange – takže zprávu dostane každá kopie.
/// </summary>
/// <remarks>
/// Doručení <b>nejvýš jednou</b>: bez inboxu, opakování a dead-letter fronty – je to jen upozornění („něco se změnilo“).
/// Ztracené upozornění dožene pojistné dotazování stránky. Chyba handleru se jen zaloguje.
/// Přirovnání: běžná fronta = úkol na nástěnce, který si vezme jeden kolega; rozeslání = hlášení rozhlasem do každé kanceláře.
/// </remarks>
public sealed class RabbitMqBroadcastConsumer(
    RabbitMqConnection connection,
    IBroadcastMessageHandler handler,
    BroadcastSubscription subscription,
    ILogger<RabbitMqBroadcastConsumer> logger) : RabbitMqQueueConsumer(connection, logger)
{
    /// <summary>Společná část jména – do metrik a tras (bez náhodné části, ať nevznikají nové řady v Grafaně).</summary>
    public const string QueuePrefix = "minishop.live";

    // Jméno jedinečné pro tuto kopii (kontejner + náhoda – dvě kopie na jednom stroji si nepřekážejí).
    private readonly string _queueName =
        $"{QueuePrefix}.{Environment.MachineName.ToLowerInvariant()}.{Guid.NewGuid().ToString("N")[..8]}";

    public override string QueueName => _queueName;

    protected override string TelemetryQueueName => QueuePrefix;

    protected override IReadOnlyCollection<string> ContractNames => subscription.ContractNames;

    /// <summary>Dočasná fronta: jen pro tohle spojení (exclusive), po odpojení ji broker smaže (autoDelete); bez dead-letter.</summary>
    protected override Task DeclareQueueAsync(IChannel channel, CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(QueueName, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);

    protected override async Task HandleAsync(IIntegrationMessage message, string payload, bool redelivered,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleAsync(message, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Nejvýš jednou – neopakovat (stránky se stejně obnoví pojistným dotazem).
            logger.LogWarning("Rozeslání zprávy {Type} se nepovedlo: {Error}", message.GetType().Name, ex.Message);
        }
    }
}
