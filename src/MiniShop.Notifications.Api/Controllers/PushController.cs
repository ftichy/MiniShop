using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Notifications.Api.Controllers;

/// <summary>
/// Mikroservisy M2C: zařízení s oznámeními push pro stránku E-maily a oznámení ve Webu. Kdo je přihlášený, ověřil Web –
/// služba dostane ID účtu v adrese (věří Webu díky servisnímu klíči). Endpoint zařízení (osobní údaj) jen v těle požadavku.
/// </summary>
[ApiController]
[Route("api/push/{userId}")]
public sealed class PushController(PushSubscriptionService subscriptions, PushNotifier notifier) : ControllerBase
{
    [HttpGet("devices")]
    public async Task<ActionResult<IReadOnlyList<PushDeviceDto>>> List(string userId, CancellationToken cancellationToken) =>
        Ok(await subscriptions.ListAsync(userId, cancellationToken));

    /// <summary>Zapnout na zařízení; neplatná adresa push služby (pravidla v doméně) = 400.</summary>
    [HttpPost("devices")]
    public async Task<IActionResult> Subscribe(string userId, PushDeviceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await subscriptions.SubscribeAsync(userId, new PushSubscribeRequest(request.Endpoint, request.P256dh, request.Auth),
                request.DeviceLabel, cancellationToken);
            return NoContent();
        }
        catch (DomainException)
        {
            return BadRequest();
        }
    }

    [HttpPost("devices/unsubscribe")]
    public async Task<IActionResult> Unsubscribe(string userId, PushEndpointRequest request, CancellationToken cancellationToken)
    {
        await subscriptions.UnsubscribeAsync(userId, request.Endpoint, cancellationToken);
        return NoContent();
    }

    [HttpDelete("devices/{id:int}")]
    public async Task<IActionResult> Remove(string userId, int id, CancellationToken cancellationToken)
    {
        await subscriptions.RemoveAsync(userId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Zkušební oznámení na všechna zařízení účtu – vrátí, na kolik jich odešlo.</summary>
    [HttpPost("test")]
    public async Task<ActionResult<PushTestResult>> Test(string userId, CancellationToken cancellationToken) =>
        Ok(new PushTestResult(await notifier.SendToCustomerAsync(userId,
            new PushMessage("MiniShop – zkouška", "Oznámení fungují. Takhle vám dáme vědět o objednávce.",
                "/Account/EmailPreferences", "test"), cancellationToken)));
}

/// <summary>Zařízení k zapnutí: <c>PushSubscription.toJSON()</c> z prohlížeče + popisek (vyrábí ho Web z User-Agent).</summary>
public sealed record PushDeviceRequest(string Endpoint, string P256dh, string Auth, string DeviceLabel);

public sealed record PushEndpointRequest(string Endpoint);

public sealed record PushTestResult(int Sent);
