using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Ordering;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Controllers;

/// <summary>
/// REST API objednávek. Účty krok 2: všechno jen pro přihlášené (fallback policy), založení jen zákazník,
/// vyřízení jen admin. Nepřihlášený dostane 401, bez oprávnění 403.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await orderService.GetAllAsync(User.ToOrderAccess(), cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(int id, CancellationToken cancellationToken) =>
        Ok(await orderService.GetByIdAsync(id, User.ToOrderAccess(), cancellationToken));

    /// <summary>
    /// Krok 9: objednávka je přijata, ale ještě není hotová – sklad ji teprve ověří (sága).
    /// Proto 202 Accepted (ne 201 Created): klient si stav zjistí na adrese z hlavičky Location
    /// (Pending → Confirmed / Rejected).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RequireCustomer)]   // účty krok 2: admin neobjednává
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orderService.PlaceOrderAsync(request, User.ToOrderCustomer(), cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpPost("{id:int}/complete")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    public async Task<ActionResult<OrderDto>> Complete(int id, CancellationToken cancellationToken) =>
        Ok(await orderService.CompleteAsync(id, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(int id, CancellationToken cancellationToken) =>
        Ok(await orderService.CancelAsync(id, User.ToOrderAccess(), cancellationToken));
}
