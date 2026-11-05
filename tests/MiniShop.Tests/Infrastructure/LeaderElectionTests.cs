using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Tests.TestDoubles;
using Npgsql;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Škálování krok 5: vedoucí kopie služby přes PostgreSQL advisory lock – vždy nejvýš jedna, po pádu převezme jiná,
/// a workery s časovačem běží jen po dobu vedení.
/// </summary>
public sealed class LeaderElectionTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly TestDatabases _databases = new();

    public void Dispose() => _databases.Dispose();

    [Fact]
    public async Task DveKopie_VedouciJeJenJedna_PoZastaveniPrevezmeDruha()
    {
        await using var first = await StartAsync("minishop.test");
        await using var second = await StartAsync("minishop.test");

        await WaitUntilAsync(() => first.Election.IsLeader || second.Election.IsLeader);
        await Task.Delay(500);   // ani po dalších pokusech druhé kopie nesmí být vedoucí obě
        Assert.True(first.Election.IsLeader ^ second.Election.IsLeader);

        var (leader, standby) = first.Election.IsLeader ? (first, second) : (second, first);
        var standbyTerm = standby.Election.WaitForLeadershipAsync(CancellationToken.None);
        Assert.False(standbyTerm.IsCompleted);

        await leader.DisposeAsync();   // řízené zastavení (nasazení, docker compose stop)

        await WaitUntilAsync(() => standby.Election.IsLeader);
        Assert.False((await standbyTerm.WaitAsync(WaitLimit)).IsCancellationRequested);
    }

    [Fact]
    public async Task VedouciPrisloOSpojeni_VedeniPusti_AJinaKopieHoPrevezme()
    {
        await using var first = await StartAsync("minishop.test");
        await WaitUntilAsync(() => first.Election.IsLeader);
        var term = await first.Election.WaitForLeadershipAsync(CancellationToken.None);

        await using var second = await StartAsync("minishop.test");

        // Pád bez rozloučení: server spojení vedoucí kopie ukončí (jako kdyby zmizel kontejner nebo síť).
        await TerminateLockHolderAsync("minishop.test");

        await WaitUntilAsync(() => term.IsCancellationRequested);   // workery první kopie se zastaví
        await WaitUntilAsync(() => second.Election.IsLeader || first.Election.IsLeader);
        await Task.Delay(500);
        Assert.True(first.Election.IsLeader ^ second.Election.IsLeader);
    }

    [Fact]
    public async Task RuzneSluzby_MajiKazdaSvehoVedouciho()
    {
        await using var web = await StartAsync("minishop.web-test");
        await using var inventory = await StartAsync("minishop.inventory-test");

        await WaitUntilAsync(() => web.Election.IsLeader && inventory.Election.IsLeader);
    }

    [Fact]
    public async Task LeaderOnlyWorker_BeziJenPoDobuVedeni_PriNovemVedeniZnovaOdZacatku()
    {
        var election = new ManualLeaderElection();
        var services = new ServiceCollection()
            .AddSingleton<ILeaderElection>(election)
            .AddSingleton<CountingWorker.Counters>()
            .AddLogging()
            .BuildServiceProvider();
        var counters = services.GetRequiredService<CountingWorker.Counters>();
        var host = ActivatorUtilities.CreateInstance<LeaderOnlyHostedService<CountingWorker>>(services);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        Assert.Equal(0, counters.Started);   // záložní kopie worker nespouští

        election.Become();
        await WaitUntilAsync(() => counters.Started == 1);

        election.Lose();
        await WaitUntilAsync(() => counters.Stopped == 1);

        election.Become();   // nové vedení = nová instance workeru
        await WaitUntilAsync(() => counters.Started == 2);

        await host.StopAsync(CancellationToken.None);   // zastavení služby zastaví i worker
        Assert.Equal(2, counters.Stopped);
        Assert.Equal(2, counters.Instances.Distinct().Count());
    }

    private async Task<RunningElection> StartAsync(string lockName)
    {
        var election = new LeaderElection(_databases.Database,
            new LeaderElectionOptions(lockName)
            {
                RetryInterval = TimeSpan.FromMilliseconds(100),
                CheckInterval = TimeSpan.FromMilliseconds(100)
            },
            NullLogger<LeaderElection>.Instance);
        await election.StartAsync(CancellationToken.None);
        return new RunningElection(election);
    }

    private async Task TerminateLockHolderAsync(string lockName)
    {
        await using var connection = _databases.Database.CreateConnection(pooling: false);
        await connection.OpenAsync();
        // Advisory lock s jedním číselným klíčem: objid = dolních 32 bitů klíče (hashtext vrací int).
        await using var command = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND objid = hashtext(@name)::oid "
            + "AND database = (SELECT oid FROM pg_database WHERE datname = current_database())", connection);
        command.Parameters.AddWithValue("name", lockName);
        Assert.Equal(true, await command.ExecuteScalarAsync());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Podmínka se nesplnila včas.");
            await Task.Delay(50);
        }
    }

    private sealed class RunningElection(LeaderElection election) : IAsyncDisposable
    {
        private bool _stopped;

        public LeaderElection Election { get; } = election;

        public async ValueTask DisposeAsync()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            await Election.StopAsync(CancellationToken.None);
            Election.Dispose();
        }
    }

    /// <summary>Vedení řízené testem (bez databáze).</summary>
    private sealed class ManualLeaderElection : ILeaderElection
    {
        private readonly Lock _gate = new();
        private TaskCompletionSource<CancellationToken> _leadership = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _term;

        public bool IsLeader => _term is not null;

        public Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return _leadership.Task.WaitAsync(cancellationToken);
            }
        }

        public void Become()
        {
            lock (_gate)
            {
                _term = new CancellationTokenSource();
                _leadership.SetResult(_term.Token);
            }
        }

        public void Lose()
        {
            lock (_gate)
            {
                _term!.Cancel();
                _term = null;
                _leadership = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private sealed class CountingWorker(CountingWorker.Counters counters) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            counters.Start(this);
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            finally
            {
                counters.Stop();
            }
        }

        public sealed class Counters
        {
            private int _started;
            private int _stopped;

            public int Started => Volatile.Read(ref _started);
            public int Stopped => Volatile.Read(ref _stopped);
            public List<object> Instances { get; } = [];

            public void Start(object worker)
            {
                lock (Instances)
                {
                    Instances.Add(worker);
                }

                Interlocked.Increment(ref _started);
            }

            public void Stop() => Interlocked.Increment(ref _stopped);
        }
    }
}
