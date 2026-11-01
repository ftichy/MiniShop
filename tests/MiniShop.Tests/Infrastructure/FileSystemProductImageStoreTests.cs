using MiniShop.Application.Inventory;
using MiniShop.Infrastructure.Inventory;

namespace MiniShop.Tests.Infrastructure;

/// <summary>E-shop krok 2B: obrázky jako soubory na disku – a pojistka proti cestám mimo složku (path traversal).</summary>
public sealed class FileSystemProductImageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "minishop-images-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Ulozeni_Precteni_Smazani()
    {
        var store = new FileSystemProductImageStore(_root);
        byte[] content = [1, 2, 3];

        await store.SaveAsync("7-0123456789ab.png", content);
        Assert.Equal(content, await store.ReadAsync("7-0123456789ab.png"));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));   // dočasný soubor po přejmenování nezůstal

        store.Delete("7-0123456789ab.png");
        Assert.Null(await store.ReadAsync("7-0123456789ab.png"));
        store.Delete("7-0123456789ab.png");   // podruhé – neexistující soubor není chyba
    }

    [Theory]
    [InlineData("../7-0123456789ab.png")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("/etc/passwd")]
    [InlineData("7-0123456789ab.png.exe")]
    public async Task NeplatneJmeno_SeKeSlozceVubecNepripoji(string fileName)
    {
        var store = new FileSystemProductImageStore(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(fileName, [1]));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(fileName));
        Assert.Throws<ArgumentException>(() => store.Delete(fileName));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "webp")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, null)]   // GIF
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]               // "<svg"
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46 }, null)]               // "RIFF" bez WEBP (např. WAV)
    public void Format_PodlePrvnichBajtu(byte[] content, string? expected)
    {
        Assert.Equal(expected, ProductImageFormat.DetectExtension(content));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
