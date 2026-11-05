using Microsoft.Extensions.Configuration;
using MiniShop.ServiceDefaults;

namespace MiniShop.Tests.Web;

/// <summary>Škálování krok 5: každá kopie služby píše log do vlastního souboru, soubory zaniklých kopií se po 7 dnech smažou.</summary>
public sealed class PerInstanceLogFilesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "minishop-logs-" + Guid.NewGuid().ToString("N"));

    public PerInstanceLogFilesTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void CestaSInstanci_DostaneJmenoKontejneru_StareSouboryJinychKopiiSmaze()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var stale = Touch("minishop-oldcontainer-20261020.log", now.AddDays(-10));
        var recent = Touch("minishop-othercopy-20261104.log", now.AddDays(-1));
        var foreign = Touch("inventory-oldcontainer-20261020.log", now.AddDays(-10));
        var configuration = Configuration(Path.Combine(_directory, "minishop-{Instance}-.log"));

        PerInstanceLogFiles.Apply(configuration, "abc123", now);

        Assert.Equal(Path.Combine(_directory, "minishop-abc123-.log"), configuration["Serilog:WriteTo:1:Args:path"]);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));    // druhá kopie (nebo nedávná) zůstává
        Assert.True(File.Exists(foreign));   // jiná služba si uklízí sama
    }

    [Fact]
    public void CestaBezInstance_ZustavaBezZmeny()
    {
        var configuration = Configuration("logs/minishop-.log");

        PerInstanceLogFiles.Apply(configuration, "abc123", DateTime.UtcNow);

        Assert.Equal("logs/minishop-.log", configuration["Serilog:WriteTo:1:Args:path"]);
    }

    private static IConfiguration Configuration(string path) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Console",
            ["Serilog:WriteTo:1:Name"] = "File",
            ["Serilog:WriteTo:1:Args:path"] = path
        })
        .Build();

    private string Touch(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "log");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
