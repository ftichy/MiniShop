using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Customers;
using MiniShop.Contracts.Invoicing;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Domain.Invoicing;

namespace MiniShop.Application.Invoicing;

/// <summary>
/// E-shop krok 15: účetní ve vedlejší kanceláři. Sleduje existující události (Ordering ani Payments se nezměnily)
/// a jakmile o objednávce ví „co a komu“ i „zaplaceno“, vystaví fakturu a oznámí ji (<see cref="InvoiceIssuedV1"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Karta: zaplaceno = <see cref="PaymentSucceededV1"/>. Dobírka a objednávky z API: zaplaceno při převzetí =
/// <see cref="OrderCompletedV1"/> (karta už v tu chvíli fakturu má – druhá nevznikne).</item>
/// <item>Zprávy mimo pořadí: platba před vznikem objednávky založí kopii a faktura vznikne, až dorazí vznik.</item>
/// <item>Faktura, číslo z řady i zpráva do outboxu jsou v jedné transakci – když cokoli selže, nevznikne nic
/// a číslo se nespotřebuje. Souběh o číslo = konflikt verze řady → výjimka → consumer zprávu zopakuje.</item>
/// </list>
/// </remarks>
public sealed class InvoicingHandler(
    IBillableOrderRepository orders,
    IInvoiceRepository invoices,
    IInvoiceNumberSeriesRepository series,
    IInvoicingMessagePublisher publisher,
    IInvoicingUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<OrderPlacedV2>,
    IIntegrationMessageHandler<PaymentSucceededV1>,
    IIntegrationMessageHandler<OrderCompletedV1>,
    IIntegrationMessageHandler<OrderRejectedV1>,
    IIntegrationMessageHandler<OrderCancelledV1>,
    IIntegrationMessageHandler<CustomerAnonymizedV1>
{
    public const string PaidByCard = "Uhrazeno kartou online";
    public const string PaidOnDelivery = "Uhrazeno při převzetí";

    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public Task HandleAsync(OrderPlacedV2 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ApplyPlaced(
            message.CustomerId,
            message.CustomerName,
            message.DeliveryAddress,
            message.ShippingMethod,
            message.ShippingPrice,
            message.TotalPrice,
            message.OccurredAtUtc,
            message.Items.Select(item => new InvoiceLine(item.ProductName, item.Quantity, item.UnitPrice, item.Discount, item.LineTotal))),
            cancellationToken);

    public Task HandleAsync(PaymentSucceededV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkPaid(message.OccurredAtUtc, PaidByCard), cancellationToken);

    public Task HandleAsync(OrderCompletedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkPaid(message.OccurredAtUtc, PaidOnDelivery), cancellationToken);

    /// <summary>Zamítnutá objednávka se nefakturuje (ani když platba dorazí pozdě – peníze vrátí sága).</summary>
    public Task HandleAsync(OrderRejectedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkVoided(), cancellationToken);

    /// <summary>Zrušená před vystavením faktury se nefakturuje; zrušení po vystavení = dobropis (krok 15B).</summary>
    public Task HandleAsync(OrderCancelledV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkVoided(), cancellationToken);

    /// <summary>Smazaný účet: kopie objednávek pryč. Vystavené faktury zůstávají – zákon je chce 10 let.</summary>
    public async Task HandleAsync(CustomerAnonymizedV1 message, CancellationToken cancellationToken)
    {
        await orders.RemoveForCustomerAsync(message.CustomerId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ChangeAsync(int orderId, Action<BillableOrder> change, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        var order = await orders.GetAsync(orderId, cancellationToken);
        if (order is null)
        {
            order = BillableOrder.Create(orderId);
            orders.Add(order);
        }

        change(order);

        if (order.IsReadyToInvoice && !await invoices.ExistsForOrderAsync(orderId, cancellationToken))
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var year = TimeZoneInfo.ConvertTimeFromUtc(now, Prague).Year;
            var yearSeries = await series.GetAsync(year, cancellationToken);
            if (yearSeries is null)
            {
                yearSeries = InvoiceNumberSeries.Start(year);
                series.Add(yearSeries);
            }

            var invoice = Invoice.Issue(order, yearSeries.Next(), now);
            invoices.Add(invoice);
            publisher.Publish(new InvoiceIssuedV1(Guid.NewGuid(), now, orderId, invoice.Number, invoice.Total));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
