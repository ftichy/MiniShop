using System.Collections.Concurrent;
using System.Diagnostics;
using MiniShop.Infrastructure.Observability;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Krok 11: "mini OpenTelemetry" pro testy – poslouchá zdroj MiniShop.Messaging a pamatuje si dokončené spany.
/// Bez posluchače by ActivitySource.StartActivity vracel null a žádné spany by nevznikly.
/// </summary>
/// <remarks>
/// Listener je globální (vidí i testy běžící souběžně) – proto se spany vždy filtrují podle trace ID testu.
/// </remarks>
public sealed class ActivityRecorder : IDisposable
{
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public ActivityRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _stopped.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Dokončené spany jedné trasy v pořadí, v jakém začaly.</summary>
    public IReadOnlyList<Activity> SpansOf(ActivityTraceId traceId) =>
        _stopped.Where(a => a.TraceId == traceId).OrderBy(a => a.StartTimeUtc).ToList();

    public void Dispose() => _listener.Dispose();
}
