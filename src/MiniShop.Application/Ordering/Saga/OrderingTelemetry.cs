using System.Diagnostics.Metrics;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Saga;

/// <summary>
/// Observabilita G2: metriky modulu Ordering. Jen třídy z .NET (System.Diagnostics.Metrics) – Application nemá balíčky;
/// OpenTelemetry v ServiceDefaults poslouchá zdroj podle jména („MiniShop.*“).
/// </summary>
public static class OrderingTelemetry
{
    public const string MeterName = "MiniShop.Ordering";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Jak dlouho trvalo od založení objednávky po potvrzení ságou – podklad pro SLO „99 % objednávek potvrzeno do 30 s“ (G3).
    /// Štítek <c>payment</c> (none/card/cashondelivery): u karty doba zahrnuje i to, jak dlouho zákazník zadával kartu.
    /// </summary>
    public static readonly Histogram<double> ConfirmationDuration = Meter.CreateHistogram<double>(
        "minishop.order.confirmation.duration", unit: "s",
        description: "Doba od založení objednávky po potvrzení ságou",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [1, 2, 5, 10, 20, 30, 60, 120, 300, 600, 1800] });

    /// <summary>Zapíše dobu potvrzení – volá orchestrátor až po úspěšném uložení (opakovaná zpráva nic nezapíše dvakrát).</summary>
    public static void RecordConfirmation(Order order, DateTime confirmedAtUtc) =>
        ConfirmationDuration.Record(
            Math.Max(0, (confirmedAtUtc - order.CreatedAtUtc).TotalSeconds),
            new KeyValuePair<string, object?>("payment", order.PaymentMethod.ToString().ToLowerInvariant()));
}
