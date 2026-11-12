using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Customers;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 7: modul Notifications – odběratel integračních událostí objednávky (pub/sub). Ordering jen oznamuje
/// fakta, tenhle handler rozhodne, komu co napsat. Nový kanál (SMS, push) = nový odběratel, Ordering se nemění.
/// </summary>
/// <remarks>
/// <para>
/// Příjemce (jméno, e-mail, souhrn) si modul uloží z <see cref="OrderPlacedV2"/>; ostatní události nesou hlavně číslo
/// objednávky. Každou zprávu obaluje inbox modulu (notifications.db) – opakované doručení e-mail znovu nepošle.
/// Známé omezení jako dřív: pád mezi odesláním e-mailu a zápisem do inboxu = e-mail podruhé (at-least-once).
/// </para>
/// <para>
/// Pořadí zpráv není zaručené: když potvrzení předběhne vznik, příjemce ještě neznáme – handler vyhodí výjimku
/// a zpráva přijde znovu (RabbitMQ za 2 s). Čeká nejvýš <see cref="MaxWaitForPlacedOrder"/>, pak to vzdá – to je
/// objednávka z doby před modulem Notifications, pro kterou OrderPlacedV2 nikdy nepřijde.
/// </para>
/// </remarks>
public sealed class OrderNotificationsHandler(
    IOrderRecipientRepository recipients,
    IBackInStockRepository backInStock,
    INotificationsUnitOfWork unitOfWork,
    IEmailSender emailSender,
    OrderEmailTemplates templates,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<OrderPlacedV2>,
    IIntegrationMessageHandler<OrderConfirmedV1>,
    IIntegrationMessageHandler<OrderRejectedV1>,
    IIntegrationMessageHandler<OrderShippedV1>,
    IIntegrationMessageHandler<OrderCompletedV1>,
    IIntegrationMessageHandler<OrderCancelledV1>,
    IIntegrationMessageHandler<CustomerAnonymizedV1>,
    IIntegrationMessageHandler<PaymentRefundedV1>,
    IIntegrationMessageHandler<ReturnRequestedV1>,
    IIntegrationMessageHandler<ReturnRejectedV1>,
    IIntegrationMessageHandler<ReturnCompletedV1>
{
    /// <summary>Jak dlouho čekat, než dorazí OrderPlacedV2 (zpráva mimo pořadí), u potvrzení a zamítnutí.</summary>
    public static readonly TimeSpan MaxWaitForPlacedOrder = TimeSpan.FromMinutes(5);

    /// <summary>Objednávka vznikla: zapamatovat si příjemce a hned napsat „přijali jsme“.</summary>
    public async Task HandleAsync(OrderPlacedV2 message, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(message.OrderId);
        var recipient = await recipients.GetAsync(message.OrderId, cancellationToken);
        if (recipient is null)
        {
            recipient = new OrderRecipient(
                message.OrderId,
                message.CustomerId,
                message.CustomerName,
                message.CustomerEmail,
                message.Items.Select(i => new OrderRecipientItem(i.ProductName, i.Quantity, i.LineTotal)),
                message.ShippingMethod,
                message.ShippingPrice,
                message.DeliveryAddress,
                message.TotalPrice);
            recipients.Add(recipient);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await SendAsync(recipient, templates.Placed, cancellationToken);
    }

    public async Task HandleAsync(OrderConfirmedV1 message, CancellationToken cancellationToken)
    {
        var recipient = await RequireRecipientAsync(message.OrderId, message.OccurredAtUtc, cancellationToken);
        await SendAsync(recipient, templates.Confirmed, cancellationToken);
    }

    public async Task HandleAsync(OrderRejectedV1 message, CancellationToken cancellationToken)
    {
        var recipient = await RequireRecipientAsync(message.OrderId, message.OccurredAtUtc, cancellationToken);
        await SendAsync(recipient, to => templates.Rejected(to, message.Reason), cancellationToken);
    }

    public async Task HandleAsync(OrderShippedV1 message, CancellationToken cancellationToken)
    {
        var to = await RecipientOrFallbackAsync(message.OrderId, message.CustomerEmail, cancellationToken);
        await SendAsync(to, recipient => templates.Shipped(recipient, message.Carrier, message.TrackingNumber), cancellationToken);
    }

    public async Task HandleAsync(OrderCompletedV1 message, CancellationToken cancellationToken) =>
        await SendAsync(await RecipientOrFallbackAsync(message.OrderId, message.CustomerEmail, cancellationToken),
            templates.Delivered, cancellationToken);

    public async Task HandleAsync(OrderCancelledV1 message, CancellationToken cancellationToken) =>
        await SendAsync(await RecipientOrFallbackAsync(message.OrderId, message.CustomerEmail, cancellationToken),
            templates.Cancelled, cancellationToken);

    /// <summary>E-shop krok 8: pozdní platba se vrací – dát vědět (zprávu posílá modul Payments, ne Ordering).</summary>
    public async Task HandleAsync(PaymentRefundedV1 message, CancellationToken cancellationToken)
    {
        var recipient = await RequireRecipientAsync(message.OrderId, message.OccurredAtUtc, cancellationToken);
        await SendAsync(recipient, to => templates.Refunded(to, message.Amount), cancellationToken);
    }

    /// <summary>E-shop krok 12: žádost o vrácení – kam a do kdy poslat zboží.</summary>
    public async Task HandleAsync(ReturnRequestedV1 message, CancellationToken cancellationToken) =>
        await SendAsync(await RecipientOrFallbackAsync(message.OrderId, message.CustomerEmail, cancellationToken),
            to => templates.ReturnRequested(to, message.SendByUtc), cancellationToken);

    public async Task HandleAsync(ReturnRejectedV1 message, CancellationToken cancellationToken) =>
        await SendAsync(await RecipientOrFallbackAsync(message.OrderId, message.CustomerEmail, cancellationToken),
            to => templates.ReturnRejected(to, message.Reason), cancellationToken);

    public async Task HandleAsync(ReturnCompletedV1 message, CancellationToken cancellationToken) =>
        await SendAsync(await RecipientOrFallbackAsync(message.OrderId, message.CustomerEmail, cancellationToken),
            to => templates.ReturnCompleted(to, message.Amount, message.RefundedByCard), cancellationToken);

    /// <summary>Zákazník smazal účet → zapomenout jeho jméno, e-mail, adresu i souhrny objednávek.</summary>
    public async Task HandleAsync(CustomerAnonymizedV1 message, CancellationToken cancellationToken)
    {
        await recipients.RemoveForCustomerAsync(message.CustomerId, cancellationToken);
        await backInStock.RemoveForCustomerAsync(message.CustomerId, cancellationToken);   // i lístečky „dejte mi vědět“
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Potvrzení a zamítnutí nesou jen číslo objednávky – bez uloženého příjemce nevíme, komu psát. Zpráva mimo pořadí:
    /// výjimka = zkusit znovu. Po <see cref="MaxWaitForPlacedOrder"/> to vzdáme (objednávka starší než Notifications).
    /// </summary>
    private async Task<OrderMailRecipient?> RequireRecipientAsync(int orderId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        if (await recipients.GetAsync(orderId, cancellationToken) is { } recipient)
        {
            return OrderMailRecipient.From(recipient);
        }

        if (timeProvider.GetUtcNow().UtcDateTime - occurredAtUtc < MaxWaitForPlacedOrder)
        {
            throw new InvalidOperationException(
                $"Pro objednávku {orderId} ještě nedorazilo OrderPlacedV2 (zpráva mimo pořadí) – zkusí se znovu.");
        }

        return null;
    }

    /// <summary>
    /// Odeslání, doručení a zrušení nesou i e-mail – u objednávek starších než Notifications (příjemce neznáme)
    /// pošleme aspoň kratší e-mail bez jména a souhrnu.
    /// </summary>
    private async Task<OrderMailRecipient> RecipientOrFallbackAsync(int orderId, string email, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        return await recipients.GetAsync(orderId, cancellationToken) is { } recipient
            ? OrderMailRecipient.From(recipient)
            : new OrderMailRecipient(orderId, email, Order: null);
    }

    private Task SendAsync(OrderRecipient recipient, Func<OrderMailRecipient, EmailMessage> template,
        CancellationToken cancellationToken) =>
        SendAsync(OrderMailRecipient.From(recipient), template, cancellationToken);

    private Task SendAsync(OrderMailRecipient? to, Func<OrderMailRecipient, EmailMessage> template,
        CancellationToken cancellationToken)
    {
        // Prázdný e-mail = zákazník smazal účet (objednávka je anonymizovaná) – není komu psát.
        if (to is null || string.IsNullOrEmpty(to.Email))
        {
            return Task.CompletedTask;
        }

        return emailSender.SendAsync(template(to), cancellationToken);
    }
}
