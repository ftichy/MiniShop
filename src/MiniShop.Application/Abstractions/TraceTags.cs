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
}
