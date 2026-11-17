using Microsoft.AspNetCore.Mvc;
using MiniShop.Infrastructure.Hosting;

namespace MiniShop.Inventory.Api.Controllers;

/// <summary>
/// Mikroservisy M2B: provoz zpráv této služby pro stránku /Admin/Messages ve Webu – outboxy modulů, poison zprávy,
/// dead-letter fronty a jejich vrácení. Jen se servisním klíčem (celé /api); kdo z lidí smí (role Provoz), rozhoduje Web.
/// Stejný controller má každá služba – logika je jednou v <see cref="MessagesOverview"/>.
/// </summary>
[ApiController]
[Route("api/messages")]
public sealed class MessagesController(MessagesOverview messages) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceMessagesStatus>> Get(CancellationToken cancellationToken) =>
        Ok(await messages.GetAsync(cancellationToken));

    [HttpPost("{module}/{id:guid}/retry")]
    public async Task<ActionResult<MessageRetryResult>> Retry(string module, Guid id, CancellationToken cancellationToken) =>
        Ok(new MessageRetryResult(await messages.RetryAsync(module, id, cancellationToken)));

    [HttpPost("queues/{queue}/requeue")]
    public async Task<ActionResult<MessageRequeueResult>> Requeue(string queue, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(new MessageRequeueResult(await messages.RequeueAsync(queue, cancellationToken)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);   // fronta nepatří této službě
        }
    }
}
