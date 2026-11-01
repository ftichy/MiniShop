using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Inventory;

namespace MiniShop.Inventory.Api.Controllers;

/// <summary>
/// REST API pro produkty. Controller jen přijme požadavek a deleguje ho na službu (Single Responsibility).
/// </summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsController(IProductService productService, IProductImageService imageService) : ControllerBase
{
    // ---------- E-shop krok 2B: obrázky ----------

    /// <summary>Nahraje obrázek (multipart, pole <c>file</c>). Limit požadavku hlídá i Kestrel – větší ani nedočte.</summary>
    [HttpPut("{id:int}/image")]
    [RequestSizeLimit(ProductImageFormat.MaxBytes + 64 * 1024)]
    public async Task<ActionResult<ProductDto>> SetImage(int id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > ProductImageFormat.MaxBytes)
        {
            return Problem("Obrázek může mít nejvýš 1 MB.", statusCode: StatusCodes.Status400BadRequest);
        }

        using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        return Ok(await imageService.SetImageAsync(id, content.ToArray(), cancellationToken));
    }

    [HttpDelete("{id:int}/image")]
    public async Task<ActionResult<ProductDto>> RemoveImage(int id, CancellationToken cancellationToken) =>
        Ok(await imageService.RemoveImageAsync(id, cancellationToken));

    /// <summary>Soubor obrázku – čte ho Web a posílá ho dál veřejně na /media/products/{soubor}.</summary>
    [HttpGet("images/{fileName}")]
    public async Task<IActionResult> GetImage(string fileName, CancellationToken cancellationToken)
    {
        var image = await imageService.GetImageAsync(fileName, cancellationToken);
        return image is null ? NotFound() : File(image.Content, image.ContentType);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await productService.GetAllAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> GetById(int id, CancellationToken cancellationToken) =>
        Ok(await productService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await productService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductDto>> Update(int id, UpdateProductRequest request, CancellationToken cancellationToken) =>
        Ok(await productService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/stock")]
    public async Task<ActionResult<ProductDto>> AddStock(int id, AddStockRequest request, CancellationToken cancellationToken) =>
        Ok(await productService.AddStockAsync(id, request.Quantity, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await productService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
