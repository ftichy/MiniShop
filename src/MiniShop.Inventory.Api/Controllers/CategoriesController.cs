using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Inventory;

namespace MiniShop.Inventory.Api.Controllers;

/// <summary>E-shop krok 2: kategorie produktů. Stejně jako produkty jen se servisním klíčem (celé /api).</summary>
[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await categoryService.GetAllAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request, CancellationToken cancellationToken) =>
        Ok(await categoryService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CategoryDto>> Rename(int id, CategoryRequest request, CancellationToken cancellationToken) =>
        Ok(await categoryService.RenameAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await categoryService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
