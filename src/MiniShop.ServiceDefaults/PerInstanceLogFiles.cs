using Microsoft.Extensions.Configuration;

namespace MiniShop.ServiceDefaults;

/// <summary>
/// Škálování krok 5: víc kopií jedné služby sdílí volume s logy (<c>/data/logs</c>). Do jednoho souboru by si psaly
/// navzájem přes sebe, proto cesta souboru Serilogu smí obsahovat <see cref="Placeholder"/> → jméno kontejneru
/// (<c>minishop-{Instance}-.log</c> → <c>minishop-3f2a9c1b7d4e-20261105.log</c>).
/// </summary>
/// <remarks>
/// Jméno kontejneru se mění s každým nasazením a Serilog maže staré soubory (7 dní) jen se svým jménem – soubory
/// kopií, které už neběží, proto po startu uklidí <see cref="Apply"/> (starší než <see cref="Retention"/>).
/// Lokálně (bez <see cref="Placeholder"/> v cestě) se nic nemění.
/// </remarks>
public static class PerInstanceLogFiles
{
    public const string Placeholder = "{Instance}";

    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public static void Apply(IConfiguration configuration, string instanceName, DateTime utcNow)
    {
        foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
        {
            var pathSection = sink.GetSection("Args:path");
            if (pathSection.Value is not { } path || !path.Contains(Placeholder, StringComparison.Ordinal))
            {
                continue;
            }

            configuration[pathSection.Path] = path.Replace(Placeholder, instanceName, StringComparison.Ordinal);
            DeleteStaleFiles(path, utcNow);
        }
    }

    private static void DeleteStaleFiles(string pathTemplate, DateTime utcNow)
    {
        try
        {
            var directory = Path.GetDirectoryName(pathTemplate);
            var fileName = Path.GetFileName(pathTemplate);
            var prefix = fileName[..fileName.IndexOf(Placeholder, StringComparison.Ordinal)];
            if (string.IsNullOrEmpty(directory) || prefix.Length == 0 || !Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, prefix + "*" + Path.GetExtension(fileName)))
            {
                if (File.GetLastWriteTimeUtc(file) < utcNow - Retention)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
            // Úklid logů nesmí zastavit start služby – zkusí se při dalším startu.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
