using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Inventory;

namespace MiniShop.Inventory.Api.Controllers;

/// <summary>E-shop krok 28A: sklady (Praha, Brno) – Web je nabízí při naskladnění. Jen se servisním klíčem jako celé /api.</summary>
[ApiController]
[Route("api/warehouses")]
public sealed class WarehousesController(IProductService productService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WarehouseDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await productService.GetWarehousesAsync(cancellationToken));
}
