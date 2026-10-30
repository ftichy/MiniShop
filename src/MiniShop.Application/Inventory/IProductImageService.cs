namespace MiniShop.Application.Inventory;

/// <summary>
/// E-shop krok 2B: obrázky produktů. Ve skladu <see cref="ProductImageService"/>, ve Webu InventoryApiClient.
/// </summary>
public interface IProductImageService
{
    /// <summary>Nahraje (nebo vymění) obrázek produktu. Nepovolený formát nebo velikost = DomainException.</summary>
    Task<ProductDto> SetImageAsync(int productId, byte[] content, CancellationToken cancellationToken = default);

    Task<ProductDto> RemoveImageAsync(int productId, CancellationToken cancellationToken = default);

    /// <returns>Obrázek, nebo null, když soubor neexistuje (nebo jméno není platné).</returns>
    Task<ProductImage?> GetImageAsync(string fileName, CancellationToken cancellationToken = default);
}

public sealed record ProductImage(byte[] Content, string ContentType);
