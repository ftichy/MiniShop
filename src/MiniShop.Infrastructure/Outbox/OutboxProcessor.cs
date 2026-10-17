using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Abstractions;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// "Pošťák" outboxu (krok 5): vybere nezpracované zprávy, převede je zpět na události a doručí je.
/// Doménové události handlerům v aplikaci, integrační (krok 7) ven přes <see cref="IIntegrationEventBus"/>.
/// Jedno zavolání <see cref="ProcessPendingAsync"/> = jedno vybrání schránky. Pravidelně ho volá OutboxWorker ve Webu.
/// </summary>
/// <remarks>
/// Singleton – DbContext a handlery jsou scoped, proto si pro každou zprávu vytvoří nový scope (nový DbContext).
/// Rozpracované změny handleru, který selhal, se tak nepřenesou do zpracování další zprávy.
/// Počítá s jedinou instancí aplikace; při více instancích by bylo potřeba zprávy zamykat.
/// </remarks>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
{
    /// <summary>Kolik zpráv se zpracuje v jednom kole.</summary>
    public const int BatchSize = 20;

    /// <summary>Po tolika neúspěšných pokusech se zpráva přestane zkoušet (poison message).</summary>
    public const int MaxAttempts = 5;

    private const int MaxErrorLength = 2000;

    /// <summary>
    /// Zpracuje jednu dávku nezpracovaných zpráv, od nejstarší. Vrací počet zpráv, které zkusil doručit
    /// (integrační zprávy přeskočené kvůli nedostupnému brokeru se nepočítají).
    /// </summary>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        List<OutboxMessage> pending;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiniShopDbContext>();
            pending = await db.OutboxMessages
                .AsNoTracking()
                .Where(m => m.ProcessedAtUtc == null && m.Attempts < MaxAttempts)
                .OrderBy(m => m.OccurredAtUtc)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
        }

        // Když broker neběží, zbylé integrační zprávy v dávce ani nezkoušíme – doménové se zpracují normálně.
        var busAvailable = true;
        var processed = 0;
        foreach (var message in pending)
        {
            if (!busAvailable && message.Kind == OutboxMessageKind.Integration)
            {
                continue;
            }

            busAvailable = await ProcessAsync(message, cancellationToken);
            processed++;
        }

        return processed;
    }

    /// <returns>false = broker není dostupný (krok 8).</returns>
    private async Task<bool> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MiniShopDbContext>();

        try
        {
            if (message.Kind == OutboxMessageKind.Integration)
            {
                // Integrační událost (krok 7) – ven pro jiné služby, handlery v aplikaci ji nedostanou.
                var integrationEvent = IntegrationEventReader.Read(message);
                await services.GetRequiredService<IIntegrationEventBus>().PublishAsync(integrationEvent, cancellationToken);
            }
            else
            {
                var domainEvent = await services.GetRequiredService<OutboxEventReader>().ReadAsync(message, cancellationToken);
                await services.GetRequiredService<IDomainEventDispatcher>().DispatchAsync([domainEvent], cancellationToken);
            }

            // Značka přes ExecuteUpdate – přímo do DB, mimo change tracker.
            var processedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await db.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.ProcessedAtUtc, processedAtUtc)
                    .SetProperty(m => m.Error, (string?)null), cancellationToken);
        }
        catch (IntegrationBusUnavailableException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Broker neběží (krok 8) – chyba není ve zprávě, takže se nepočítá jako pokus a zpráva nepropadne
            // jako poison message. Počká v outboxu, dokud se broker nevrátí.
            await db.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Error, Truncate(ex.Message)), cancellationToken);

            logger.LogWarning("Broker není dostupný, integrační zprávy počkají v outboxu: {Error}", ex.Message);
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Handler selhal (dispatcher už chybu zalogoval). Zpráva zůstane nezpracovaná a zkusí se v dalším kole.
            var attempts = message.Attempts + 1;
            var error = Truncate(ex.Message);
            await db.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Attempts, attempts)
                    .SetProperty(m => m.Error, error), cancellationToken);

            if (attempts >= MaxAttempts)
            {
                logger.LogError("Zpráva {MessageId} ({Type}) selhala {Attempts}× a už se nebude zkoušet (poison message): {Error}",
                    message.Id, message.Type, attempts, error);
            }
            else
            {
                logger.LogWarning("Zpráva {MessageId} ({Type}) selhala ({Attempts}/{MaxAttempts}), zkusí se znovu: {Error}",
                    message.Id, message.Type, attempts, MaxAttempts, error);
            }
        }

        return true;
    }

    private static string Truncate(string error) => error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
}
