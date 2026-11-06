using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Invoicing;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 15: faktura je vystavená → e-mail s PDF v příloze. Příjemce z kopie <c>OrderPlacedV2</c> (jako ostatní
/// e-maily o objednávce), PDF přes <see cref="IInvoiceDocuments"/>.
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
    TimeProvider timeProvider) : IIntegrationMessageHandler<InvoiceIssuedV1>
{
    public async Task HandleAsync(InvoiceIssuedV1 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        var recipient = await recipients.GetAsync(message.OrderId, cancellationToken);
        if (recipient is null)
        {
            if (timeProvider.GetUtcNow().UtcDateTime - message.OccurredAtUtc < OrderNotificationsHandler.MaxWaitForPlacedOrder)
            {
                throw new InvalidOperationException(
                    $"Pro objednávku {message.OrderId} ještě nedorazilo OrderPlacedV2 (zpráva mimo pořadí) – zkusí se znovu.");
            }

            return;   // objednávka bez příjemce (smazaný účet, starší než Notifications)
        }

        if (string.IsNullOrEmpty(recipient.CustomerEmail))
        {
            return;
        }

        var pdf = await documents.GetInvoiceAsync(message.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Faktura {message.InvoiceNumber} k objednávce {message.OrderId} nebyla nalezena.");

        await emailSender.SendAsync(
            templates.Invoice(OrderMailRecipient.From(recipient), message.InvoiceNumber, pdf), cancellationToken);
    }
}
