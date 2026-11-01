using MiniShop.Application.Inventory;
using MiniShop.Web.Inventory;

namespace MiniShop.Web.Catalog;

/// <summary>
/// E-shop krok 2B: veřejná adresa obrázků produktů <c>/media/products/{soubor}</c>. Inventory.Api z internetu dostupná
/// není – obrázek stáhne Web se servisním klíčem (<see cref="InventoryApiClient"/>) a pošle ho dál.
/// </summary>
/// <remarks>
/// Jméno souboru obsahuje otisk obsahu (nový obrázek = nové jméno), takže se pod jednou adresou nikdy nezmění.
/// Prohlížeč (i případná cache po cestě) si ho proto smí uložit na rok bez ptaní (<c>immutable</c>) – na sklad
/// se tak sahá jen při prvním zobrazení. Neplatné jméno = 404 bez dotazu na sklad.
/// </remarks>
public static class ProductImageEndpoint
{
    public const string PathPrefix = "/media/products/";

    public static IEndpointRouteBuilder MapProductImages(this IEndpointRouteBuilder app)
    {
        app.MapGet(PathPrefix + "{fileName}", GetAsync).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> GetAsync(string fileName, IProductImageService images, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!ProductImageFormat.IsValidFileName(fileName))
        {
            return Results.NotFound();
        }

        ProductImage? image;
        try
        {
            image = await images.GetImageAsync(fileName, cancellationToken);
        }
        catch (InventoryUnavailableException)
        {
            // Sklad teď neodpovídá – výloha běží dál, jen bez obrázku. Tuhle odpověď si nikdo nesmí zapamatovat.
            context.Response.Headers.CacheControl = "no-store";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (image is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.File(image.Content, image.ContentType);
    }
}
