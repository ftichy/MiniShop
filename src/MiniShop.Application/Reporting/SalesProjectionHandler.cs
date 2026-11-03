using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Domain.Reporting;

namespace MiniShop.Application.Reporting;

/// <summary>
/// E-shop krok 11 (CQRS): projekce přehledu tržeb – odběratel událostí, které už existují kvůli jiným modulům.
/// Ordering ani Payments se kvůli ní nezměnily (nový odběratel = nic neměnit u odesílatele).
/// </summary>
/// <remarks>
/// <para>
/// Pořadí není zaručené: chybí-li řádek, založí se (<see cref="SalesOrder.Create"/>) a vznik ho doplní později.
/// Stav necouvá (<see cref="SalesOrder.ApplyStatus"/>), takže pozdní nebo duplicitní zpráva nic nerozbije.
/// Stejnou zprávu dvakrát zachytí už inbox modulu (reporting.db).
/// </para>
/// <para><see cref="OrderSnapshotV1"/> přichází při přestavění přehledu a zpracuje se stejně jako vznik + stav.</para>
/// </remarks>
public sealed class SalesProjectionHandler(
    ISalesOrderRepository orders,
    IReportingUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<OrderPlacedV2>,
    IIntegrationMessageHandler<OrderConfirmedV1>,
    IIntegrationMessageHandler<OrderRejectedV1>,
    IIntegrationMessageHandler<OrderCancelledV1>,
    IIntegrationMessageHandler<OrderCompletedV1>,
    IIntegrationMessageHandler<PaymentRefundedV1>,
    IIntegrationMessageHandler<OrderSnapshotV1>,
    IIntegrationMessageHandler<ReturnCompletedV1>
{
    public Task HandleAsync(OrderPlacedV2 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ApplyPlaced(message.OccurredAtUtc, message.TotalPrice,
            message.ShippingPrice, ToLines(message.Items), UtcNow), cancellationToken);

    public Task HandleAsync(OrderConfirmedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ApplyStatus(SalesOrderStatus.Confirmed, UtcNow), cancellationToken);

    public Task HandleAsync(OrderRejectedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ApplyStatus(SalesOrderStatus.Rejected, UtcNow), cancellationToken);

    public Task HandleAsync(OrderCancelledV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ApplyStatus(SalesOrderStatus.Cancelled, UtcNow), cancellationToken);

    public Task HandleAsync(OrderCompletedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ApplyStatus(SalesOrderStatus.Completed, UtcNow), cancellationToken);

    /// <summary>E-shop krok 12: vrácená objednávka – z tržby zmizí (dobírka i karta).</summary>
    public Task HandleAsync(ReturnCompletedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.ApplyStatus(SalesOrderStatus.Returned, UtcNow), cancellationToken);

    /// <summary>Vrácené peníze (pozdní platba, zrušená zaplacená objednávka) – posílá je modul Payments.</summary>
    public Task HandleAsync(PaymentRefundedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.AddRefund(message.Amount, UtcNow), cancellationToken);

    /// <summary>Přestavění přehledu: snímek = detaily + stav (stav jen dopředu, kdyby mezitím dorazila novější událost).</summary>
    public Task HandleAsync(OrderSnapshotV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order =>
        {
            order.ApplyPlaced(message.PlacedAtUtc, message.TotalPrice, message.ShippingPrice, ToLines(message.Items), UtcNow);
            order.ApplyStatus(ToStatus(message.Status), UtcNow);
        }, cancellationToken);

    /// <summary>Stav Orderingu → zjednodušený stav přehledu (čekání na platbu, balení a odeslání admina tady nezajímá).</summary>
    public static SalesOrderStatus ToStatus(string orderStatus) => orderStatus switch
    {
        "Confirmed" or "Packing" or "Shipped" => SalesOrderStatus.Confirmed,
        "Completed" => SalesOrderStatus.Completed,
        "Rejected" => SalesOrderStatus.Rejected,
        "Cancelled" => SalesOrderStatus.Cancelled,
        "Returned" => SalesOrderStatus.Returned,             // e-shop krok 12
        "ReturnRequested" => SalesOrderStatus.Completed,     // vrácení teprve probíhá – tržba zatím platí
        _ => SalesOrderStatus.Placed   // Pending, AwaitingPayment
    };

    private async Task ChangeAsync(int orderId, Action<SalesOrder> change, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        var order = await orders.GetAsync(orderId, cancellationToken);
        if (order is null)
        {
            order = SalesOrder.Create(orderId, UtcNow);   // zpráva mimo pořadí – detaily doplní vznik nebo snímek
            orders.Add(order);
        }

        change(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<SalesOrderLine> ToLines(IEnumerable<OrderItemV2> items) =>
        items.Select(i => new SalesOrderLine(i.ProductId, i.ProductName, i.Quantity, i.LineTotal));

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
