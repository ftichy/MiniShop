namespace MiniShop.Application.Inventory;

/// <summary>
/// E-shop krok 2B: kde leží soubory obrázků produktů (sklad). Implementace: složka na disku (volume skladu).
/// Jména souborů vždy splňují <see cref="ProductImageFormat.IsValidFileName"/> – úložiště jiná odmítne.
/// </summary>
public interface IProductImageStore
{
    Task SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);

    /// <returns>Obsah souboru, nebo null, když neexistuje.</returns>
    Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Smaže soubor; neexistující soubor není chyba.</summary>
    void Delete(string fileName);
}
