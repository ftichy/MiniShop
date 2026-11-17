using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniShop.Tests.Architecture;

/// <summary>
/// Observabilita G2/G3: dashboard a alarmy pro Grafanu jsou v gitu (deploy/grafana) a do Grafany se importují ručně.
/// Test hlídá, co by se při importu poznalo až pozdě: platný JSON s volbou zdroje dat, a že každý alarm má podmínku
/// trvání a český popis „co se děje a co dělat“. Syntaxi PromQL kontroluje v CI promtool (job Build a testy).
/// </summary>
public sealed partial class GrafanaConfigTests
{
    private static readonly string GrafanaDirectory = Path.Combine(FindRepositoryRoot(), "deploy", "grafana");

    [Fact]
    public void Dashboard_PlatnyJson_ZdrojDatSeVybiraPriImportu()
    {
        using var dashboard = JsonDocument.Parse(File.ReadAllText(Path.Combine(GrafanaDirectory, "minishop-zpravy-a-sagy.json")));
        var root = dashboard.RootElement;

        Assert.Equal("minishop-zpravy-a-sagy", root.GetProperty("uid").GetString());   // stejné uid = nový import přepíše
        Assert.Equal("DS_PROMETHEUS", root.GetProperty("__inputs")[0].GetProperty("name").GetString());

        var panels = root.GetProperty("panels").EnumerateArray().Where(p => p.GetProperty("type").GetString() != "row").ToList();
        Assert.NotEmpty(panels);
        Assert.All(panels, panel =>
            Assert.Equal("${DS_PROMETHEUS}", panel.GetProperty("datasource").GetProperty("uid").GetString()));
        Assert.Equal(panels.Count, panels.Select(p => p.GetProperty("id").GetInt32()).Distinct().Count());
    }

    /// <summary>Mikroservisy M0: alarmy limitů (jiný zdroj dat → vlastní soubor a složka) hlídá test i promtool v CI.</summary>
    private static string[] AlertFileNames() =>
        Directory.GetFiles(GrafanaDirectory, "minishop-alarmy*.yaml").Select(f => Path.GetFileName(f)).Order().ToArray();

    public static TheoryData<string> AlertFiles() => new(AlertFileNames());

    [Fact]
    public void Alarmy_VsechnySouboryKontrolujeCi()
    {
        var ci = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("minishop-alarmy-limity.yaml", AlertFileNames());
        Assert.All(AlertFileNames(), file => Assert.Contains($"/rules/{file}", ci));
    }

    [Theory]
    [MemberData(nameof(AlertFiles))]
    public void Alarmy_KazdyMaTrvaniPopisANavod(string file)
    {
        var text = File.ReadAllText(Path.Combine(GrafanaDirectory, file));
        var rules = AlertStart().Split(text).Skip(1).ToList();

        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            Assert.Matches(AlertName(), rule);   // jméno bez mezer a diakritiky
            Assert.Contains("expr:", rule);
            Assert.Contains("for:", rule);
            Assert.Contains("summary:", rule);
            Assert.Contains("description:", rule);
            Assert.Contains("runbook_url:", rule);
        });
        Assert.DoesNotContain("limit:", text);   // import do Grafana-managed rules „limit“ nepodporuje
    }

    [GeneratedRegex(@"^\s*- alert:", RegexOptions.Multiline)]
    private static partial Regex AlertStart();

    [GeneratedRegex(@"^ MiniShop[A-Za-z0-9]+\r?\n")]
    private static partial Regex AlertName();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MiniShop.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Kořen repozitáře (MiniShop.slnx) nenalezen.");
    }
}
