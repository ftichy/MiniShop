using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Persistence;
using Npgsql;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Škálování krok 5: která kopie služby je „vedoucí“ (leader) – jen ta spouští workery s časovačem
/// (<see cref="LeaderOnlyHostedService{TWorker}"/>).
/// </summary>
public interface ILeaderElection
{
    /// <summary>Je tahle kopie právě vedoucí?</summary>
    bool IsLeader { get; }

    /// <summary>
    /// Počká, až se tahle kopie stane vedoucí. Vrátí token, který se zruší ve chvíli, kdy vedení ztratí
    /// (spadlé spojení s databází, zastavení služby).
    /// </summary>
    Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken);
}

/// <summary>Jméno zámku (jedno pro každou službu) a jak často se zkouší / kontroluje.</summary>
public sealed record LeaderElectionOptions(string LockName)
{
    /// <summary>Jak často záložní kopie zkouší zámek získat (= nejdelší doba bez vedoucího po pádu).</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Jak často vedoucí kopie ověřuje, že spojení se zámkem žije.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Volba vedoucí kopie přes PostgreSQL <b>advisory lock</b> (škálování krok 5). Přirovnání: v kanceláři je víc úředníků,
/// klíč od pokladny ale jen jeden – kdo ho drží, vydává peníze. Klíčem je zámek <c>pg_try_advisory_lock</c>
/// na vlastním spojení: drží ho jen jedna kopie a drží ho, dokud spojení žije.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Zámek patří spojení (relaci), ne transakci – proto vlastní spojení mimo pool EF Core, otevřené po celou dobu vedení.</item>
/// <item>Kopie spadne / zastaví se → spojení zanikne → PostgreSQL zámek sám uvolní a nejdéle do <see cref="LeaderElectionOptions.RetryInterval"/>
///   ho získá jiná kopie. Žádná tabulka, žádné „vypršení“ záznamu, nic navíc ke spuštění.</item>
/// <item>Vedoucí každých <see cref="LeaderElectionOptions.CheckInterval"/> ověří spojení (<c>SELECT 1</c>). Když selže,
///   vedení hned pustí (workery zastaví) – server zámek uvolní, jakmile zjistí, že je spojení mrtvé.</item>
/// <item>Krátký překryv dvou vedoucích (stará kopie ještě dokončuje kolo, nová už začala) není chyba: zprávy jsou idempotentní
///   (inbox) a souběh hlídají concurrency tokeny – zámek jen zabraňuje zbytečné práci navíc.</item>
/// </list>
/// </remarks>
public sealed class LeaderElection(
    ModuleDatabase database,
    LeaderElectionOptions options,
    ILogger<LeaderElection> logger) : BackgroundService, ILeaderElection
{
    private readonly Lock _gate = new();
    private TaskCompletionSource<CancellationToken> _leadership = NewLeadership();
    private CancellationTokenSource? _term;

    public bool IsLeader
    {
        get
        {
            lock (_gate)
            {
                return _term is not null;
            }
        }
    }

    public Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken)
    {
        Task<CancellationToken> leadership;
        lock (_gate)
        {
            leadership = _leadership.Task;
        }

        return leadership.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nebrzdit start aplikace.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = database.CreateConnection(pooling: false);
                await connection.OpenAsync(stoppingToken);

                if (await TryLockAsync(connection, stoppingToken))
                {
                    BecomeLeader();
                    try
                    {
                        await HoldAsync(connection, stoppingToken);
                    }
                    finally
                    {
                        // Nejdřív zastavit workery, teprve pak (Dispose spojení) uvolnit zámek pro jinou kopii.
                        LoseLeadership();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Nedostupná databáze, přerušené spojení… Zkusí se to znovu za chvíli.
                logger.LogWarning(ex, "[Vedoucí] Zámek {LockName} se nepodařilo získat nebo udržet", options.LockName);
            }

            try
            {
                await Task.Delay(options.RetryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> TryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // hashtext = číslo zámku ze jména (advisory lock má jako klíč číslo). Vrací true/false hned, nečeká.
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtext(@name))", connection);
        command.Parameters.AddWithValue("name", options.LockName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task HoldAsync(NpgsqlConnection connection, CancellationToken stoppingToken)
    {
        while (true)
        {
            await Task.Delay(options.CheckInterval, stoppingToken);

            // Spojení žije = zámek pořád držíme. Selhání (výjimka) = vedení končí.
            await using var command = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = 5 };
            await command.ExecuteScalarAsync(stoppingToken);
        }
    }

    private void BecomeLeader()
    {
        lock (_gate)
        {
            _term = new CancellationTokenSource();
            _leadership.TrySetResult(_term.Token);
        }

        logger.LogInformation("[Vedoucí] Instance {Instance} je vedoucí (zámek {LockName}) – spouští workery s časovačem",
            Environment.MachineName, options.LockName);
    }

    private void LoseLeadership()
    {
        CancellationTokenSource? term;
        lock (_gate)
        {
            term = _term;
            _term = null;
            _leadership = NewLeadership();
        }

        if (term is null)
        {
            return;
        }

        logger.LogInformation("[Vedoucí] Instance {Instance} už není vedoucí (zámek {LockName})", Environment.MachineName, options.LockName);
        // Bez Dispose: token si drží workery (propojené tokeny) – zrušený zdroj stačí uklidit GC.
        term.Cancel();
    }

    private static TaskCompletionSource<CancellationToken> NewLeadership() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
