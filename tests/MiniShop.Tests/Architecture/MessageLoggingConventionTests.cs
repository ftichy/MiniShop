using System.Text.RegularExpressions;

namespace MiniShop.Tests.Architecture;

/// <summary>
/// Proud B3: obsah integrační zprávy nikdy do logu – jen typ a ID. Zprávy nesou osobní údaje (OrderPlacedV2: jméno,
/// e-mail, adresa) a logy jdou do souborů na VM i do Grafana Cloud. Auditní fronta z EDA kroku 8 je takhle logovala.
/// Kdo potřebuje vidět obsah, má stránku Proud zpráv (maskuje osobní údaje – <c>PersonalDataMask</c>).
/// </summary>
public sealed partial class MessageLoggingConventionTests
{
    [Fact]
    public void ZdrojoveKody_NelogujiObsahZprav()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => PayloadInLogTemplate().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Obsah zprávy ({Payload}) nepatří do logu – jen typ a ID: " + string.Join(", ", offenders));
    }

    [GeneratedRegex(@"\.Log\w*\(""[^""]*\{Payload\}")]
    private static partial Regex PayloadInLogTemplate();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniShop.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Kořen repozitáře nenalezen.");
    }
}
