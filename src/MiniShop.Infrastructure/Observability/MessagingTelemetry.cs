using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MiniShop.Infrastructure.Observability;

/// <summary>
/// Krok 11: trasy (spany) a metriky zpráv – outbox, RabbitMQ, consumery. Jen třídy z .NET (System.Diagnostics),
/// žádný balíček OpenTelemetry: ten je jen ve službách (ServiceDefaults) a "poslouchá" zdroj podle jména.
/// </summary>
/// <remarks>
/// <b>Trace ID jako podací číslo zásilky.</b> HTTP požadavek "POST /api/orders" dostane trace ID. Kdo na trase
/// něco udělá, otevře span (úsek) se stejným trace ID. Aby se řetěz nepřetrhl na místech, kde zpráva "leží"
/// (outbox, fronta RabbitMQ), musí se trace ID uložit ke zprávě a příjemce z něj pokračuje:
/// <list type="number">
///   <item>uložení do outboxu – <see cref="CurrentTraceParent"/> do sloupce OutboxMessage.TraceParent,</item>
///   <item>doručení z outboxu – span "publish …" s rodičem z TraceParent (<see cref="StartOutboxActivity"/>),</item>
///   <item>RabbitMQ – hlavička "traceparent" ve zprávě, consumer otevře span "process …" (<see cref="StartProcessActivity"/>),</item>
///   <item>handler uloží do outboxu další zprávu – a ta znovu dostane totéž trace ID (bod 1).</item>
/// </list>
/// Když nikdo neposlouchá (testy bez listeneru, služba bez OpenTelemetry), StartActivity vrací null a nic se neděje.
/// </remarks>
public static class MessagingTelemetry
{
    /// <summary>Jméno zdroje tras i metrik – ServiceDefaults ho zapíná přes "MiniShop.*".</summary>
    public const string SourceName = "MiniShop.Messaging";

    /// <summary>Název hlavičky podle standardu W3C Trace Context (00-{trace id}-{span id}-{příznaky}).</summary>
    public const string TraceParentHeader = "traceparent";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    /// <summary>Zprávy úspěšně doručené z outboxu (tag message.type, module).</summary>
    public static readonly Counter<long> OutboxDelivered = Meter.CreateCounter<long>(
        "minishop.outbox.delivered", unit: "{message}", description: "Zprávy doručené z outboxu");

    /// <summary>Neúspěšné pokusy o doručení z outboxu (selhal handler nebo broker zprávu odmítl).</summary>
    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>(
        "minishop.outbox.failed", unit: "{message}", description: "Neúspěšné pokusy o doručení z outboxu");

    /// <summary>Jak dlouho zpráva čekala v outboxu, než odešla – ukazuje zpoždění workeru (~0–2 s).</summary>
    public static readonly Histogram<double> OutboxDelay = Meter.CreateHistogram<double>(
        "minishop.outbox.delay", unit: "s", description: "Doba od vzniku zprávy po doručení z outboxu");

    /// <summary>Zprávy zpracované consumerem (tag message.type, queue).</summary>
    public static readonly Counter<long> ConsumerHandled = Meter.CreateCounter<long>(
        "minishop.consumer.handled", unit: "{message}", description: "Zprávy zpracované z fronty");

    /// <summary>Zprávy, které consumer nezpracoval (vrátil do fronty nebo zahodil).</summary>
    public static readonly Counter<long> ConsumerFailed = Meter.CreateCounter<long>(
        "minishop.consumer.failed", unit: "{message}", description: "Zprávy, které se z fronty nepodařilo zpracovat");

    /// <summary>
    /// "Podací číslo" aktuální operace (HTTP požadavku, handleru) ve formátu W3C traceparent;
    /// null = nic se nesleduje. Ukládá se ke každé nové zprávě v outboxu.
    /// </summary>
    public static string? CurrentTraceParent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.Id : null;

    /// <summary>
    /// Span doručení zprávy z outboxu. Rodič = operace, která zprávu vytvořila (uložený traceparent) –
    /// trasa tak pokračuje, i když zpráva v outboxu ležela několik sekund.
    /// </summary>
    /// <param name="operation">"publish" (integrační zpráva ven) nebo "dispatch" (doménová událost handlerům).</param>
    public static Activity? StartOutboxActivity(string operation, string type, Guid messageId, string? traceParent, string module)
    {
        var activity = ActivitySource.StartActivity(
            $"{operation} {type}",
            operation == "publish" ? ActivityKind.Producer : ActivityKind.Internal,
            ParseParent(traceParent));

        activity?.SetTag("messaging.operation.name", operation);
        activity?.SetTag("messaging.destination.name", type);
        activity?.SetTag("messaging.message.id", messageId.ToString());
        activity?.SetTag("minishop.module", module);
        return activity;
    }

    /// <summary>Span zpracování přijaté zprávy (consumer fronty nebo modul v procesu). Rodič = odesílatel.</summary>
    public static Activity? StartProcessActivity(string type, Guid messageId, string? traceParent, string queue)
    {
        var activity = ActivitySource.StartActivity($"process {type}", ActivityKind.Consumer, ParseParent(traceParent));

        activity?.SetTag("messaging.operation.name", "process");
        activity?.SetTag("messaging.destination.name", type);
        activity?.SetTag("messaging.message.id", messageId.ToString());
        activity?.SetTag("messaging.consumer.group.name", queue);
        return activity;
    }

    /// <summary>Označí span jako chybný – v dashboardu bude červeně.</summary>
    public static void RecordError(this Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);
    }

    /// <summary>Neplatný nebo chybějící traceparent = bez rodiče (span začne novou trasu).</summary>
    private static ActivityContext ParseParent(string? traceParent) =>
        ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var context) ? context : default;
}
