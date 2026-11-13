namespace MiniShop.Tests.Architecture;

/// <summary>
/// Čas se zobrazuje jen přes <c>CzechTime</c>. Metoda ToLocalTime bere zónu stroje, a kontejner v produkci běží v UTC
/// (chyba z issue #100: časy o 1–2 hodiny pozadu).
/// </summary>
public sealed class TimeConventionTests
{
    [Fact]
    public void ZdrojaneKody_NepouzivajiToLocalTime()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".cshtml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(".ToLocalTime(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Použijte CzechTime místo ToLocalTime: " + string.Join(", ", offenders));
    }

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
