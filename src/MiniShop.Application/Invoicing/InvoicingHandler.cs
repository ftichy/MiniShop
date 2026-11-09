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
/// E-shop krok 15B: když zaplacená objednávka s fakturou skončí zrušením, zamítnutím nebo vrácením, vystaví k faktuře
/// dobropis (<see cref="CreditNoteIssuedV1"/>) – fakturu nepřepisuje.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Karta: zaplaceno = <see cref="PaymentSucceededV1"/>. Dobírka a objednávky z API: zaplaceno při převzetí =
/// <see cref="OrderCompletedV1"/> (karta už v tu chvíli fakturu má – druhá nevznikne).</item>
/// <item>Zprávy mimo pořadí: platba před vznikem objednávky založí kopii a faktura vznikne, až dorazí vznik.
/// Vratka před fakturou (teoreticky) = faktura a hned po ní dobropis.</item>
/// <item>Doklad, číslo z řady i zpráva do outboxu jsou v jedné transakci – když cokoli selže, nevznikne nic
/// a číslo se nespotřebuje. Souběh o číslo = konflikt verze řady → výjimka → consumer zprávu zopakuje.</item>
/// </list>
/// </remarks>
public sealed class InvoicingHandler(
    IBillableOrderRepository orders,
    IInvoiceRepository invoices,
    IInvoiceNumberSeriesRepository series,
    ICreditNoteRepository creditNotes,
    ICreditNoteNumberSeriesRepository creditNoteSeries,
    IInvoicingMessagePublisher publisher,
    IInvoicingUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<OrderPlacedV2>,
    IIntegrationMessageHandler<PaymentSucceededV1>,
    IIntegrationMessageHandler<OrderCompletedV1>,
    IIntegrationMessageHandler<OrderRejectedV1>,
    IIntegrationMessageHandler<OrderCancelledV1>,
    IIntegrationMessageHandler<ReturnCompletedV1>,
    IIntegrationMessageHandler<CustomerAnonymizedV1>
{
    public const string PaidByCard = "Uhrazeno kartou online";
    public const string PaidOnDelivery = "Uhrazeno při převzetí";

    /// <summary>E-shop krok 15B: zrušenou/zamítnutou zaplacenou objednávku vrací sága na kartu (dobírka faktury před doručením nemá).</summary>
    public const string RefundedToCard = "Vráceno na kartu";

    /// <summary>E-shop krok 15B: vratka dobírky – peníze posílá admin ručně.</summary>
    public const string RefundedByTransfer = "Vrátíme převodem na účet";

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

    /// <summary>
    /// Zamítnutá objednávka se nefakturuje (ani když platba dorazí pozdě – peníze vrátí sága). Stihla-li pozdní platba
    /// fakturu vystavit dřív, než zamítnutí dorazilo, vznikne dobropis (krok 15B).
    /// </summary>
    public Task HandleAsync(OrderRejectedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkVoided(CreditNoteReason.Rejected, RefundedToCard), cancellationToken);

    /// <summary>Zrušená před vystavením faktury se nefakturuje; zrušení po vystavení = dobropis (krok 15B).</summary>
    public Task HandleAsync(OrderCancelledV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkVoided(CreditNoteReason.Cancelled, RefundedToCard), cancellationToken);

    /// <summary>E-shop krok 15B: vrácené zboží = dobropis k faktuře (kartou vráceno na kartu, dobírka převodem).</summary>
    public Task HandleAsync(ReturnCompletedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkReturned(message.RefundedByCard ? RefundedToCard : RefundedByTransfer),
            cancellationToken);

    /// <summary>Smazaný účet: kopie objednávek pryč. Vystavené faktury a dobropisy zůstávají – zákon je chce 10 let.</summary>
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var year = TimeZoneInfo.ConvertTimeFromUtc(now, Prague).Year;

        Invoice? newInvoice = null;
        if (order.IsReadyToInvoice && !await invoices.ExistsForOrderAsync(orderId, cancellationToken))
        {
            var yearSeries = await series.GetAsync(year, cancellationToken);
            if (yearSeries is null)
            {
                yearSeries = InvoiceNumberSeries.Start(year);
                series.Add(yearSeries);
            }

            newInvoice = Invoice.Issue(order, yearSeries.Next(), now);
            invoices.Add(newInvoice);
            publisher.Publish(new InvoiceIssuedV1(Guid.NewGuid(), now, orderId, newInvoice.Number, newInvoice.Total));
        }

        // Krok 15B: dobropis jen k existující faktuře (i k té, která vznikla právě teď) a jen jednou.
        if (order.CreditReason is { } reason
            && (newInvoice ?? await invoices.GetByOrderAsync(orderId, cancellationToken)) is { } invoice
            && !await creditNotes.ExistsForOrderAsync(orderId, cancellationToken))
        {
            var yearSeries = await creditNoteSeries.GetAsync(year, cancellationToken);
            if (yearSeries is null)
            {
                yearSeries = CreditNoteNumberSeries.Start(year);
                creditNoteSeries.Add(yearSeries);
            }

            var creditNote = CreditNote.Issue(invoice, yearSeries.Next(), reason, order.RefundNote ?? string.Empty, now);
            creditNotes.Add(creditNote);
            publisher.Publish(new CreditNoteIssuedV1(Guid.NewGuid(), now, orderId, creditNote.Number, invoice.Number, creditNote.Total));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
