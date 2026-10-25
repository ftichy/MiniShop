using System.Text;
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
        ["journey-event-types"] = JourneyEventTypes
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
