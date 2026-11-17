using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;
using MiniShop.Web.Internal;

namespace MiniShop.Web.Controllers;

/// <summary>
/// Mikroservisy M2A: interní API Webu pro modul Notifications – stejné odpovědi jako implementace v procesu
/// (<see cref="ICustomerContacts"/>, <see cref="IStaffDirectory"/>, <see cref="IProductNames"/>,
/// <see cref="IInvoiceDocuments"/>), jen přes HTTP. Žádná nová logika: pravidla (jen potvrzený e-mail, role při odeslání,
/// blokace) zůstávají na jednom místě. Dokud Notifications běží ve Webu (do M2B), API nikdo nevolá – ověří se předem.
/// </summary>
/// <remarks>
/// Jen se servisním klíčem (<see cref="InternalApi.PolicyName"/>). V adrese jen ID – e-mail a jméno jsou jen v těle
/// odpovědi, do trasy ani do logu se nedostanou. Neexistující = 404 (klient z toho udělá null jako dnes).
/// </remarks>
[ApiController]
[Route("internal")]
[Authorize(Policy = InternalApi.PolicyName)]
public sealed class InternalController : ControllerBase
{
    [HttpGet("customers/{customerId}/contact")]
    public async Task<ActionResult<CustomerContact>> GetContact(
        string customerId, [FromServices] ICustomerContacts contacts, CancellationToken cancellationToken) =>
        await contacts.FindAsync(customerId, cancellationToken) is { } contact ? Ok(contact) : NotFound();

    [HttpPost("staff/allowed")]
    public async Task<ActionResult<IReadOnlyList<string>>> FilterAllowedStaff(
        StaffAllowedRequest request, [FromServices] IStaffDirectory staff, CancellationToken cancellationToken) =>
        Ok(await staff.FilterAllowedAsync(request.Kind, request.UserIds, cancellationToken));

    [HttpGet("products/{productId:int}/name")]
    public async Task<ActionResult<ProductNameResponse>> GetProductName(
        int productId, [FromServices] IProductNames products, CancellationToken cancellationToken) =>
        await products.FindAsync(productId, cancellationToken) is { } name ? Ok(new ProductNameResponse(name)) : NotFound();

    [HttpGet("orders/{orderId:int}/invoice")]
    public async Task<IActionResult> GetInvoice(
        int orderId, [FromServices] IInvoiceDocuments documents, CancellationToken cancellationToken) =>
        Pdf(await documents.GetInvoiceAsync(orderId, cancellationToken));

    [HttpGet("orders/{orderId:int}/credit-notes/{number}")]
    public async Task<IActionResult> GetCreditNote(
        int orderId, string number, [FromServices] IInvoiceDocuments documents, CancellationToken cancellationToken) =>
        Pdf(await documents.GetCreditNoteAsync(orderId, number, cancellationToken));

    private IActionResult Pdf(EmailAttachment? attachment) =>
        attachment is null ? NotFound() : File(attachment.Content, attachment.ContentType, attachment.FileName);
}

/// <summary>Mikroservisy M2A: kdo z přihlášených k oznámení k práci ho smí dostat teď (<see cref="IStaffDirectory"/>).</summary>
public sealed record StaffAllowedRequest(StaffAlertKind Kind, IReadOnlyList<string> UserIds);

/// <summary>Mikroservisy M2A: název produktu (<see cref="IProductNames"/>).</summary>
public sealed record ProductNameResponse(string Name);
