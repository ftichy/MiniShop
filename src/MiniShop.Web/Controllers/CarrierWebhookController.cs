using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Infrastructure.Shipping;

namespace MiniShop.Web.Controllers;

/// <summary>
/// E-shop krok 26C: webhook dopravce – sem posílá simulovaný dopravce (carrier-sim) změny stavu zásilky. Bez přihlášení
/// (dopravce účet nemá), každá zpráva se ověřuje podpisem v hlavičce <see cref="CarrierWebhookProcessor.SignatureHeaderName"/>.
/// </summary>
/// <remarks>
/// Dostupný i z internetu jako webhook Stripu – chrání ho podpis s časem, ne skrytá adresa. Tělo se čte jako text
/// (podpis se počítá z přesných bajtů). 200 = převzato, 401 = podpis/čas, 400 = nečitelné, 503 = zkuste znovu.
/// </remarks>
[ApiController]
[Route("api/shipping/carrier-webhook")]
[AllowAnonymous]
public sealed class CarrierWebhookController(CarrierWebhookProcessor processor) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers[CarrierWebhookProcessor.SignatureHeaderName].FirstOrDefault();

        return await processor.ProcessAsync(body, signature, cancellationToken) switch
        {
            CarrierWebhookResult.Accepted => Ok(),
            CarrierWebhookResult.InvalidSignature => Unauthorized(),
            CarrierWebhookResult.RetryLater => StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => BadRequest()
        };
    }
}
