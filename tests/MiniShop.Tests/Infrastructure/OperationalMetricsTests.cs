using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Observability;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Observabilita G2: snímek stavu pro Grafanu – hlásí se jen čerstvý, po ztrátě vedení nic, selhaný zdroj neshodí ostatní.
/// </summary>
public sealed class OperationalMetricsTests : IDisposable
{
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly OperationalMetrics _metrics;

    public OperationalMetricsTests() => _metrics = new OperationalMetrics(_time);

    private static readonly KeyValuePair<string, object?> Ordering = new("module", "Ordering");

    [Fact]
    public void Snimek_HlasiJenCerstvy()
    {
        _metrics.Update([new(OperationalMetrics.OutboxPending, 3, Ordering), new(OperationalMetrics.OutboxPoison, 1, Ordering)]);

        Assert.Equal(3, Assert.Single(_metrics.Current(OperationalMetrics.OutboxPending)).Value);
        Assert.Empty(_metrics.Current(OperationalMetrics.QueueBacklog));

        // Worker přestal měřit (kopie už není vedoucí, nebo se zasekl) – zamrzlá čísla se nehlásí.
        _time.Advance(OperationalMetrics.MaxAge + TimeSpan.FromSeconds(1));
        Assert.Empty(_metrics.Current(OperationalMetrics.OutboxPending));
    }

    [Fact]
    public void Clear_PoZtrateVedeni_NicNehlasi()
    {
        _metrics.Update([new(OperationalMetrics.OutboxPending, 3, Ordering)]);
        _metrics.Clear();

        Assert.Empty(_metrics.Current(OperationalMetrics.OutboxPending));
    }

    [Fact]
    public async Task Worker_SelhanyZdroj_OstatniSeHlasiDal()
    {
        var worker = new OperationalMetricsWorker(
            [new FailingSource(), new FixedSource(new OperationalReading(OperationalMetrics.QueueDeadLetters, 2,
                new KeyValuePair<string, object?>("queue", "minishop.ordering")))],
            _metrics, NullLogger<OperationalMetricsWorker>.Instance);

        await worker.CollectAsync(CancellationToken.None);

        Assert.Equal(2, Assert.Single(_metrics.Current(OperationalMetrics.QueueDeadLetters)).Value);
    }

    public void Dispose() => _metrics.Dispose();

    private sealed class FailingSource : IOperationalMetricsSource
    {
        public string Name => "rabbitmq";

        public Task<IReadOnlyList<OperationalReading>> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Broker nedostupný");
    }

    private sealed class FixedSource(params OperationalReading[] readings) : IOperationalMetricsSource
    {
        public string Name => "pevny";

        public Task<IReadOnlyList<OperationalReading>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperationalReading>>(readings);
    }
}
