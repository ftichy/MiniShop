using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Contracts;
using MiniShop.Application.Services;

namespace MiniShop.Web.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await orderService.GetAllAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(int id, CancellationToken cancellationToken) =>
        Ok(await orderService.GetByIdAsync(id, cancellationToken));

    /// <summary>
    /// Krok 9: objednávka je přijata, ale ještě není hotová – sklad ji teprve ověří (sága).
    /// Proto 202 Accepted (ne 201 Created): klient si stav zjistí na adrese z hlavičky Location
    /// (Pending → Confirmed / Rejected).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orderService.PlaceOrderAsync(request, cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpPost("{id:int}/complete")]
    public async Task<ActionResult<OrderDto>> Complete(int id, CancellationToken cancellationToken) =>
        Ok(await orderService.CompleteAsync(id, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(int id, CancellationToken cancellationToken) =>
        Ok(await orderService.CancelAsync(id, cancellationToken));
}
