using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Payments;

namespace MiniShop.Web.Controllers;

/// <summary>
/// E-shop krok 8: webhook platební brány – brána sem pošle výsledek platby (HTTP požadavek z jejího serveru).
/// Bez přihlášení (brána účet nemá), proto se každá zpráva ověřuje podpisem HMAC v hlavičce
/// <see cref="WebhookSignature.HeaderName"/>: špatný nebo chybějící podpis = 401 a nic se nezmění.
/// </summary>
/// <remarks>
/// Brány posílají znovu, dokud nedostanou 2xx – opakovaný webhook proto vrací 200 a nic nezdvojí.
/// Tělo se čte jako text: podpis se počítá z přesných bajtů, ne z přeparsovaného JSONu.
/// </remarks>
[ApiController]
[Route("api/payments/webhook")]
[AllowAnonymous]
public sealed class PaymentsWebhookController(PaymentWebhookProcessor processor, PaymentGatewayInfo gateway) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        // E-shop krok 16: se Stripem platí jen jeho webhook (/api/payments/stripe-webhook) – formát simulace se nepřijímá.
        if (gateway.IsStripe)
        {
            return NotFound();
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers[WebhookSignature.HeaderName].FirstOrDefault();

        return await processor.ProcessAsync(body, signature, cancellationToken) switch
        {
            PaymentWebhookResult.Accepted => Ok(),
            PaymentWebhookResult.InvalidSignature => Unauthorized(),
            PaymentWebhookResult.UnknownPayment => NotFound(),
            _ => BadRequest()
        };
    }
}
