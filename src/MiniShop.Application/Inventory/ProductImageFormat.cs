using System.Text.RegularExpressions;

namespace MiniShop.Application.Inventory;

/// <summary>
/// E-shop krok 2B: pravidla pro obrázky produktů – co se smí nahrát a jak se soubor jmenuje.
/// </summary>
/// <remarks>
/// Typ se pozná podle prvních bajtů souboru („magic bytes“), ne podle přípony nebo Content-Type od prohlížeče –
/// ty si útočník napíše, jaké chce. Povolené jsou jen rastrové formáty; SVG ne, protože může obsahovat JavaScript (XSS).
/// Jméno souboru vždy skládá server (<see cref="FileName"/>) a čte se jen jméno přesně podle vzoru – žádné „../“.
/// </remarks>
public static partial class ProductImageFormat
{
    /// <summary>Největší povolený obrázek: 1 MB.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Přípona podle obsahu (<c>jpg</c>, <c>png</c>, <c>webp</c>), nebo null pro nepovolený formát.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "jpg";
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "png";
        }

        // WebP: "RIFF" + 4 bajty délky + "WEBP"
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return "webp";
        }

        return null;
    }

    /// <summary>
    /// Jméno souboru: ID produktu + začátek SHA-256 obsahu. Stejný obrázek = stejné jméno, jiný = jiné –
    /// proto si ho prohlížeč smí uložit natrvalo (immutable) a nikdy neukáže starou verzi.
    /// </summary>
    public static string FileName(int productId, ReadOnlySpan<byte> content, string extension) =>
        $"{productId}-{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content))[..12]}.{extension}";

    /// <summary>Je to jméno, které mohl vytvořit <see cref="FileName"/>? Cokoli jiného (cesty, „..“) se odmítne.</summary>
    public static bool IsValidFileName(string? fileName) => fileName is not null && FileNamePattern().IsMatch(fileName);

    public static string ContentType(string fileName) => Path.GetExtension(fileName) switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => throw new ArgumentException($"Neznámý typ obrázku: {fileName}", nameof(fileName))
    };

    [GeneratedRegex(@"^\d{1,9}-[0-9a-f]{12}\.(jpg|png|webp)$")]
    private static partial Regex FileNamePattern();
}
