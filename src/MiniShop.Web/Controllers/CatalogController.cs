using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Web.Security;

namespace MiniShop.Web.Controllers;

/// <summary>
/// E-shop krok 21: našeptávač hledání (search-suggest.js). Veřejný jako výloha, čte jen kopii katalogu.
/// </summary>
/// <remarks>
/// Odpověď je malá (pár kategorií a nejvýš 6 produktů) a bez osobních údajů. Limit požadavků podle IP brání tomu,
/// aby někdo endpoint použil na zahlcení databáze (každé písmeno = dotaz).
/// </remarks>
[ApiController]
[Route("api/catalog")]
[AllowAnonymous]
public sealed class CatalogController(ICatalogQueryService catalog) : ControllerBase
{
    [HttpGet("suggest")]
    [EnableRateLimiting(CatalogRateLimiting.PolicyName)]
    public async Task<ActionResult<CatalogSuggestions>> Suggest([FromQuery] string? q, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";   // výsledek závisí na aktuální nabídce a dostupnosti
        return Ok(await catalog.SuggestAsync(q is { Length: > 100 } ? q[..100] : q, cancellationToken));
    }
}
