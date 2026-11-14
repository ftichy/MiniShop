using System.Diagnostics;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Business klíče na aktuálním spanu (Activity.Current – HTTP požadavek, doručení nebo zpracování zprávy).
/// Podle nich se v Aspire dashboardu (Traces → filtr) dá najít trasa např. pro objednávku 2.
/// </summary>
/// <remarks>
/// Jen třída z .NET (System.Diagnostics), žádný balíček. Když nikdo neposlouchá, Activity.Current je null a nic se neděje.
/// </remarks>
public static class TraceTags
{
    /// <summary>Číslo objednávky – Ordering i Inventory (zná ho z příkazů ReserveStockV1/ReleaseStockV1).</summary>
    public const string OrderId = "minishop.order.id";

    /// <summary>Označí aktuální span číslem objednávky.</summary>
    public static void TagOrder(int orderId) => Activity.Current?.SetTag(OrderId, orderId);

    /// <summary>
    /// Business údaj na aktuální span (jméno „minishop.…“). Do tras jdou do Grafana Cloud – <b>nikdy osobní údaje</b>
    /// (jméno, e-mail, adresa, telefon, text recenze, údaje o kartě); stačí ID a čísla, detail je v MiniShopu.
    /// Částky jako double (OpenTelemetry decimal nezná).
    /// </summary>
    public static void Tag(string name, object? value) => Activity.Current?.SetTag(name, value);

    /// <summary>Řádky „produkt×kusy“ do jednoho atributu (např. „12×2, 15×1“) – ID produktů, ne názvy.</summary>
    public static string Lines(IEnumerable<(int ProductId, int Quantity)> lines) =>
        string.Join(", ", lines.Select(line => $"{line.ProductId}×{line.Quantity}"));
}
