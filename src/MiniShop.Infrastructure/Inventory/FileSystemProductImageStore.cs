using MiniShop.Application.Inventory;

namespace MiniShop.Infrastructure.Inventory;

/// <summary>
/// E-shop krok 2B: obrázky produktů jako soubory ve složce (v kontejneru <c>/data/images</c> na volume skladu,
/// lokálně <c>images</c> vedle Inventory.Api). Zálohuje je <c>deploy/backup.py</c> spolu s databázemi.
/// </summary>
/// <remarks>
/// Pojistka proti path traversal: pracuje jen se jmény podle <see cref="ProductImageFormat.IsValidFileName"/> –
/// „../appsettings.json“ ani absolutní cesta se ke složce vůbec nepřipojí.
/// Zápis jde přes dočasný soubor a přejmenování, takže čtenář nikdy neuvidí napůl zapsaný obrázek.
/// </remarks>
public sealed class FileSystemProductImageStore(string rootPath) : IProductImageStore
{
    public async Task SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var path = PathOf(fileName);
        Directory.CreateDirectory(rootPath);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = PathOf(fileName);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    public void Delete(string fileName) => File.Delete(PathOf(fileName));   // neexistující soubor File.Delete nevadí

    private string PathOf(string fileName) => ProductImageFormat.IsValidFileName(fileName)
        ? Path.Combine(rootPath, fileName)
        : throw new ArgumentException($"Neplatné jméno souboru obrázku: {fileName}", nameof(fileName));
}
