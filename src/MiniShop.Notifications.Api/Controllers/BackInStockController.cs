using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Notifications;

namespace MiniShop.Notifications.Api.Controllers;

/// <summary>
/// Mikroservisy M2C: „Dejte mi vědět“ (e-shop krok 18) pro detail produktu ve Webu. Že jde o zákazníka a že je produkt
/// vyprodaný, ověřuje Web (zná účty i kopii katalogu).
/// </summary>
[ApiController]
[Route("api/back-in-stock/{userId}/{productId:int}")]
public sealed class BackInStockController(BackInStockService backInStock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BackInStockStatus>> Get(string userId, int productId, CancellationToken cancellationToken) =>
        Ok(new BackInStockStatus(await backInStock.IsSubscribedAsync(userId, productId, cancellationToken)));

    [HttpPut]
    public async Task<IActionResult> Subscribe(string userId, int productId, BackInStockRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await backInStock.SubscribeAsync(userId, productId, request.ProductName, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Dvojklik: druhý požadavek narazil na unikátní index zákazník + produkt – lísteček už založil první.
        }

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Cancel(string userId, int productId, CancellationToken cancellationToken)
    {
        await backInStock.CancelAsync(userId, productId, cancellationToken);
        return NoContent();
    }
}

public sealed record BackInStockStatus(bool Subscribed);

public sealed record BackInStockRequest(string ProductName);
