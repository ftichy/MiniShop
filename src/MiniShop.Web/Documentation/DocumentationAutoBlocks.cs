using System.Text;
using Microsoft.Extensions.DependencyModel;
using MiniShop.Contracts;
using MiniShop.Domain.Common;
using MiniShop.Domain.Journeys;

namespace MiniShop.Web.Documentation;

/// <summary>
/// Automatické bloky příručky: části, které se dají vyčíst přímo z kódu, se nepíšou ručně, ale generují
/// při každém zobrazení stránky. V Markdownu se zapíšou na samostatný řádek jako <c>{{auto:název}}</c>.
/// </summary>
/// <remarks>
/// Přibude-li kontrakt nebo doménová událost, objeví se v příručce sám – text kolem ale musí doplnit člověk
/// (hlídají to testy v <c>DocumentationTests</c>).
/// </remarks>
public static class DocumentationAutoBlocks
{
    private static readonly IReadOnlyDictionary<string, Func<string>> Blocks = new Dictionary<string, Func<string>>
    {
        ["contracts"] = Contracts,
        ["domain-events"] = DomainEvents,
        ["journey-event-types"] = JourneyEventTypes,
        ["nuget-packages"] = NuGetPackages
    };

    public static IReadOnlyCollection<string> Names => Blocks.Keys.ToList();

    public static string Render(string name) =>
        Blocks.TryGetValue(name, out var block)
            ? block()
            : $"> **Neznámý automatický blok `{name}`.** Známé bloky: {string.Join(", ", Blocks.Keys)}.\n";

    /// <summary>Integrační zprávy z MiniShop.Contracts: kontrakt, druh, oblast a pole.</summary>
    private static string Contracts()
    {
        var messages = typeof(IIntegrationMessage).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationMessage).IsAssignableFrom(t))
            .OrderBy(t => t.Namespace).ThenBy(t => t.Name);

        var table = new StringBuilder()
            .AppendLine("| Kontrakt | Druh | Oblast | Pole |")
            .AppendLine("| --- | --- | --- | --- |");
        foreach (var type in messages)
        {
            var kind = typeof(IIntegrationCommand).IsAssignableFrom(type) ? "příkaz" : "událost";
            var fields = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters()
                .Select(p => $"`{p.Name}`: {FriendlyName(p.ParameterType)}");
            table.AppendLine($"| `{type.Name}` | {kind} | {LastNamespaceSegment(type)} | {string.Join(", ", fields)} |");
        }

        return table.AppendLine().AppendLine("*Tabulka je vygenerovaná z kódu (`MiniShop.Contracts`) při zobrazení stránky.*").ToString();
    }

    /// <summary>Doménové události (třídy implementující IDomainEvent) podle modulu.</summary>
    private static string DomainEvents()
    {
        var events = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .OrderBy(t => t.Namespace).ThenBy(t => t.Name);

        var table = new StringBuilder()
            .AppendLine("| Doménová událost | Modul (namespace) |")
            .AppendLine("| --- | --- |");
        foreach (var type in events)
        {
            table.AppendLine($"| `{type.Name}` | `{type.Namespace}` |");
        }

        return table.AppendLine().AppendLine("*Tabulka je vygenerovaná z kódu (`MiniShop.Domain`) při zobrazení stránky.*").ToString();
    }

    /// <summary>Typy událostí proudu Journeys v pořadí, ve kterém jsou v enumu (nové jen na konec).</summary>
    private static string JourneyEventTypes() =>
        string.Join(" → ", Enum.GetNames<JourneyEventType>().Select(n => $"`{n}`"))
        + "\n\n*Seznam je vygenerovaný z `JourneyEventType` při zobrazení stránky.*\n";

    /// <summary>
    /// NuGet balíčky, na které přímo odkazují projekty MiniShop.* – i s verzí, která skutečně běží.
    /// Zdroj je <c>*.deps.json</c> spuštěné aplikace (vzniká při buildu), takže tabulka nikdy nelže.
    /// </summary>
    private static string NuGetPackages()
    {
        var context = DependencyContext.Default;
        if (context is null)
        {
            return "> Seznam balíčků není k dispozici (chybí soubor *.deps.json).\n";
        }

        var packages = context.RuntimeLibraries
            .Where(l => l.Type == "package")
            .ToDictionary(l => l.Name, l => l.Version, StringComparer.OrdinalIgnoreCase);

        var usage = context.RuntimeLibraries
            .Where(l => l.Type == "project" && l.Name.StartsWith("MiniShop.", StringComparison.Ordinal))
            .SelectMany(project => project.Dependencies
                .Where(d => packages.ContainsKey(d.Name))
                .Select(d => (Package: d.Name, Project: project.Name)))
            .GroupBy(x => x.Package, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var table = new StringBuilder()
            .AppendLine("| Balíček | Verze | Projekty |")
            .AppendLine("| --- | --- | --- |");
        foreach (var group in usage)
        {
            var projects = string.Join(", ", group.Select(x => x.Project).Distinct().Order().Select(p => $"`{p}`"));
            table.AppendLine($"| [{group.Key}](https://www.nuget.org/packages/{group.Key}) | {packages[group.Key]} | {projects} |");
        }

        return table.AppendLine()
            .AppendLine("*Tabulka je vygenerovaná ze souboru `*.deps.json` běžící aplikace – verze odpovídají nasazenému buildu.*")
            .ToString();
    }

    private static string LastNamespaceSegment(Type type) => type.Namespace?.Split('.')[^1] ?? string.Empty;

    /// <summary>IReadOnlyList`1[StockLineV1] → IReadOnlyList&lt;StockLineV1&gt; (escapované pro tabulku v Markdownu).</summary>
    private static string FriendlyName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}&lt;{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}&gt;";
    }
}
