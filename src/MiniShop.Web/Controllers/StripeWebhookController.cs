using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Payments;
using MiniShop.Infrastructure.Payments.Stripe;

namespace MiniShop.Web.Controllers;

/// <summary>
/// E-shop krok 16: webhook Stripu – sem posílá Stripe výsledek platby (zaplaceno, vypršelo). Bez přihlášení (Stripe účet
/// nemá), každá zpráva se ověřuje podpisem v hlavičce <see cref="StripeWebhookProcessor.SignatureHeaderName"/>.
/// </summary>
/// <remarks>
/// 200 = převzato (i opakovaná zpráva, cizí session, nezajímavá událost), 401 = špatný podpis nebo stará zpráva,
/// 400 = nečitelné tělo. Bez Stripu (simulovaná brána) endpoint neexistuje → 404.
/// Tělo se čte jako text – podpis se počítá z přesných bajtů.
/// </remarks>
[ApiController]
[Route("api/payments/stripe-webhook")]
[AllowAnonymous]
public sealed class StripeWebhookController(IServiceProvider services) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (services.GetService<StripeWebhookProcessor>() is not { } processor)
        {
            return NotFound();
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers[StripeWebhookProcessor.SignatureHeaderName].FirstOrDefault();

        return await processor.ProcessAsync(body, signature, cancellationToken) switch
        {
            PaymentWebhookResult.Accepted => Ok(),
            PaymentWebhookResult.InvalidSignature => Unauthorized(),
            PaymentWebhookResult.UnknownPayment => NotFound(),
            _ => BadRequest()
        };
    }
}
