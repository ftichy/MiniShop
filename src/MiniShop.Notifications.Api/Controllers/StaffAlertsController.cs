using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Notifications.Api.Controllers;

/// <summary>
/// Mikroservisy M2C: oznámení k práci (e-shop krok 24) pro stránku E-maily a oznámení. Role zná jen Web – pošle,
/// které typy smí zaměstnanec zapnout (<c>Allowed</c>); při odeslání role ověřuje znovu (interní API Webu).
/// </summary>
[ApiController]
[Route("api/staff-alerts/{userId}")]
public sealed class StaffAlertsController(StaffAlertService staffAlerts) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<StaffAlertKind>>> Get(string userId, CancellationToken cancellationToken) =>
        Ok((await staffAlerts.GetAsync(userId, cancellationToken)).ToList());

    [HttpPut]
    public async Task<IActionResult> Set(string userId, StaffAlertsRequest request, CancellationToken cancellationToken)
    {
        await staffAlerts.SetAsync(userId, request.Wanted, request.Allowed, cancellationToken);
        return NoContent();
    }
}

public sealed record StaffAlertsRequest(IReadOnlyCollection<StaffAlertKind> Wanted, IReadOnlyCollection<StaffAlertKind> Allowed);
