using System.Diagnostics.Metrics;

namespace MiniShop.Infrastructure.Observability;

/// <summary>
/// Observabilita G2: metriky <b>stavu</b> („kolik zpráv teď čeká“), ne jen událostí („zpráva doručena“). Podklad pro
/// dashboard „Zprávy a ságy“ v Grafaně a pro alarmy (G3).
/// </summary>
/// <remarks>
/// <para>
/// Přirovnání: čítače z kroku 11 jsou počítadlo projetých aut, tyhle metriky jsou pohled na parkoviště – kolik aut
/// tam právě stojí. Stav se zjišťuje dotazem do databáze a RabbitMQ; ten dělá <see cref="Hosting.OperationalMetricsWorker"/>
/// <b>jen na vedoucí kopii</b> služby (jinak by dvě kopie Webu nahlásily totéž dvakrát a Grafana by to sečetla).
/// </para>
/// <para>
/// Worker každých 30 s uloží poslední hodnoty sem a OpenTelemetry si je při exportu přečte přes observable gauge.
/// Starý snímek (worker nejede – kopie přestala být vedoucí) se nehlásí vůbec, aby v Grafaně nezůstala „zamrzlá“ čísla.
/// </para>
/// </remarks>
public sealed class OperationalMetrics : IDisposable
{
    /// <summary>Starší snímek se nehlásí (worker měří každých 30 s; pár vynechaných kol se toleruje).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    public const string OutboxPending = "minishop.outbox.pending";
    public const string OutboxPoison = "minishop.outbox.poison";
    public const string QueueBacklog = "minishop.queue.backlog";
    public const string QueueDeadLetters = "minishop.queue.dead_letters";
    public const string SagaActive = "minishop.saga.active";

    private readonly Meter _meter = new(MessagingTelemetry.SourceName);
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private IReadOnlyList<OperationalReading> _readings = [];
    private DateTimeOffset _updatedAt = DateTimeOffset.MinValue;

    public OperationalMetrics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        Gauge(OutboxPending, "{message}", "Nezpracované zprávy v outboxu modulu (i čekající na další pokus)");
        Gauge(OutboxPoison, "{message}", "Zprávy outboxu, které vyčerpaly pokusy a čekají na člověka");
        Gauge(QueueBacklog, "{message}", "Zprávy čekající ve frontě modulu v RabbitMQ (consumer nestíhá nebo neběží)");
        Gauge(QueueDeadLetters, "{message}", "Zprávy v dead-letter frontě modulu (minishop.{modul}.dead)");
        Gauge(SagaActive, "{saga}", "Rozpracované ságy podle stavu (objednávky a vratky)");
    }

    /// <summary>Nový snímek od workeru – nahradí celý předchozí (co v něm chybí, už se nehlásí).</summary>
    public void Update(IReadOnlyList<OperationalReading> readings)
    {
        lock (_lock)
        {
            _readings = readings;
            _updatedAt = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>Kopie přestala měřit (není vedoucí) – nic nehlásit.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _readings = [];
            _updatedAt = DateTimeOffset.MinValue;
        }
    }

    /// <summary>Aktuální hodnoty jedné metriky (prázdné, když je snímek starý). Veřejné kvůli testům.</summary>
    public IReadOnlyList<OperationalReading> Current(string metric)
    {
        lock (_lock)
        {
            return _timeProvider.GetUtcNow() - _updatedAt > MaxAge
                ? []
                : _readings.Where(r => r.Metric == metric).ToList();
        }
    }

    private void Gauge(string name, string unit, string description) =>
        _meter.CreateObservableGauge(name,
            () => Current(name).Select(r => new Measurement<long>(r.Value, r.Tags)),
            unit, description);

    public void Dispose() => _meter.Dispose();
}

/// <summary>Jedna hodnota stavu: metrika (<see cref="OperationalMetrics"/>), číslo a štítky (nikdy ID – limit řad).</summary>
public sealed record OperationalReading(string Metric, long Value, params KeyValuePair<string, object?>[] Tags);

/// <summary>
/// Zdroj stavu pro <see cref="OperationalMetrics"/> – outbox modulů, fronty RabbitMQ, ságy Orderingu… Každý modul/služba
/// přidá svůj (<c>AddSingleton&lt;IOperationalMetricsSource, …&gt;</c>); worker je obejde všechny.
/// </summary>
public interface IOperationalMetricsSource
{
    /// <summary>Jméno pro log, když zdroj selže.</summary>
    string Name { get; }

    Task<IReadOnlyList<OperationalReading>> ReadAsync(CancellationToken cancellationToken);
}
