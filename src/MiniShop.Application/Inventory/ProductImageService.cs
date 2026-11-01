using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// E-shop krok 2B: obrázky produktů ve skladu. Soubor jde do <see cref="IProductImageStore"/>, do produktu (a do zprávy
/// ProductUpdatedV2) jen jeho jméno – binární data do zpráv nepatří.
/// </summary>
/// <remarks>
/// Pořadí je záměrné: nejdřív soubor, pak databáze (a s ní zpráva v outboxu), nakonec smazat starý soubor.
/// Když Web dostane zprávu s novým jménem, soubor už existuje. Spadne-li uložení do databáze, zůstane jen
/// nepoužitý soubor (neškodí); kdyby se starý soubor mazal dřív, výloha by mohla ukazovat chybějící obrázek.
/// </remarks>
public sealed class ProductImageService(
    IProductRepository products,
    IProductImageStore images,
    IInventoryUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IProductImageService
{
    public async Task<ProductDto> SetImageAsync(int productId, byte[] content, CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
        {
            throw new DomainException("Vyberte soubor s obrázkem.");
        }

        if (content.Length > ProductImageFormat.MaxBytes)
        {
            throw new DomainException("Obrázek může mít nejvýš 1 MB.");
        }

        var extension = ProductImageFormat.DetectExtension(content)
            ?? throw new DomainException("Povolené jsou jen obrázky JPEG, PNG a WebP.");

        var product = await GetProductAsync(productId, cancellationToken);
        var fileName = ProductImageFormat.FileName(product.Id, content, extension);
        if (fileName == product.ImageFileName)
        {
            return product.ToDto();   // stejný obrázek znovu – nic se nemění, žádná zpráva
        }

        var previous = product.ImageFileName;
        await images.SaveAsync(fileName, content, cancellationToken);
        product.SetImage(fileName, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ProductUpdatedV1 + V2 (s novým jménem souboru)

        DeleteQuietly(previous);
        return product.ToDto();
    }

    public async Task<ProductDto> RemoveImageAsync(int productId, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, cancellationToken);
        var previous = product.ImageFileName;
        product.RemoveImage(UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        DeleteQuietly(previous);
        return product.ToDto();
    }

    public async Task<ProductImage?> GetImageAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (!ProductImageFormat.IsValidFileName(fileName))
        {
            return null;
        }

        var content = await images.ReadAsync(fileName, cancellationToken);
        return content is null ? null : new ProductImage(content, ProductImageFormat.ContentType(fileName));
    }

    /// <summary>Starý soubor až po uložení. Když smazání selže, zůstane jen nepoužitý soubor – nová data jsou v pořádku.</summary>
    private void DeleteQuietly(string? fileName)
    {
        if (fileName is null)
        {
            return;
        }

        try
        {
            images.Delete(fileName);
        }
        catch (IOException)
        {
            // Úklid není kritický – obrázek už nikdo nepoužívá.
        }
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<Product> GetProductAsync(int id, CancellationToken cancellationToken) =>
        await products.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Produkt s ID {id} nebyl nalezen.");
}
