using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Invoicing;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 15: faktura je vystavená → e-mail s PDF v příloze. E-shop krok 15B: totéž pro dobropis.
/// Příjemce z kopie <c>OrderPlacedV2</c> (jako ostatní e-maily o objednávce), PDF přes <see cref="IInvoiceDocuments"/>.
/// </summary>
/// <remarks>
/// Zpráva mimo pořadí (příjemce zatím neznáme) = výjimka a opakování, nejvýš <see cref="OrderNotificationsHandler.MaxWaitForPlacedOrder"/>.
/// Smazaný účet (příjemce zapomenutý) = nic. Inbox modulu zahodí opakované doručení – druhý e-mail nepřijde.
/// </remarks>
public sealed class InvoiceEmailHandler(
    IOrderRecipientRepository recipients,
    IInvoiceDocuments documents,
    IEmailSender emailSender,
    OrderEmailTemplates templates,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<InvoiceIssuedV1>,
    IIntegrationMessageHandler<CreditNoteIssuedV1>
{
    public async Task HandleAsync(InvoiceIssuedV1 message, CancellationToken cancellationToken)
    {
        if (await FindRecipientAsync(message.OrderId, message.OccurredAtUtc, cancellationToken) is not { } recipient)
        {
            return;
        }

        var pdf = await documents.GetInvoiceAsync(message.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Faktura {message.InvoiceNumber} k objednávce {message.OrderId} nebyla nalezena.");

        await emailSender.SendAsync(templates.Invoice(recipient, message.InvoiceNumber, pdf), cancellationToken);
    }

    /// <summary>E-shop krok 15B: dobropis v příloze.</summary>
    public async Task HandleAsync(CreditNoteIssuedV1 message, CancellationToken cancellationToken)
    {
        if (await FindRecipientAsync(message.OrderId, message.OccurredAtUtc, cancellationToken) is not { } recipient)
        {
            return;
        }

        var pdf = await documents.GetCreditNoteAsync(message.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Dobropis {message.CreditNoteNumber} k objednávce {message.OrderId} nebyl nalezen.");

        await emailSender.SendAsync(
            templates.CreditNote(recipient, message.CreditNoteNumber, message.InvoiceNumber, pdf), cancellationToken);
    }

    /// <summary>Příjemce e-mailu, nebo null (nikomu neposílat). Ještě neznámý příjemce = výjimka → opakování.</summary>
    private async Task<OrderMailRecipient?> FindRecipientAsync(int orderId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        var recipient = await recipients.GetAsync(orderId, cancellationToken);
        if (recipient is null)
        {
            if (timeProvider.GetUtcNow().UtcDateTime - occurredAtUtc < OrderNotificationsHandler.MaxWaitForPlacedOrder)
            {
                throw new InvalidOperationException(
                    $"Pro objednávku {orderId} ještě nedorazilo OrderPlacedV2 (zpráva mimo pořadí) – zkusí se znovu.");
            }

            return null;   // objednávka bez příjemce (smazaný účet, starší než Notifications)
        }

        return string.IsNullOrEmpty(recipient.CustomerEmail) ? null : OrderMailRecipient.From(recipient);
    }
}
