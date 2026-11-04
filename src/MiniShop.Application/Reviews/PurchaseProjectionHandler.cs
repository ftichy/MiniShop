using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Customers;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Reviews;

namespace MiniShop.Application.Reviews;

/// <summary>
/// E-shop krok 14: kopie nákupů pro modul Reviews z událostí, které už existují (event-carried state transfer) –
/// Ordering se kvůli hodnocení nezměnil, jen odpovídá na žádost o snímky (<see cref="OrderSnapshotV2"/>).
/// </summary>
/// <remarks>
/// Zprávy mimo pořadí: chybí-li řádek, založí se (doručení před vznikem) a vznik ho doplní. Inbox modulu zahodí
/// duplicitní doručení. Smazaný účet (<see cref="CustomerAnonymizedV1"/>) = zapomenout nákupy a skrýt jméno autora.
/// </remarks>
public sealed class PurchaseProjectionHandler(
    IPurchaseRepository purchases,
    IReviewRepository reviews,
    IReviewsUnitOfWork unitOfWork) :
    IIntegrationMessageHandler<OrderPlacedV2>,
    IIntegrationMessageHandler<OrderCompletedV1>,
    IIntegrationMessageHandler<ReturnCompletedV1>,
    IIntegrationMessageHandler<OrderSnapshotV2>,
    IIntegrationMessageHandler<CustomerAnonymizedV1>
{
    public Task HandleAsync(OrderPlacedV2 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId,
            order => order.ApplyPlaced(message.CustomerId, message.CustomerName, message.Items.Select(i => i.ProductId)),
            cancellationToken);

    /// <summary>Doručeno – od teď smí zákazník produkty z objednávky hodnotit.</summary>
    public Task HandleAsync(OrderCompletedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkDelivered(), cancellationToken);

    /// <summary>Vráceno (e-shop krok 12) – nové hodnocení už ne (napsané zůstává).</summary>
    public Task HandleAsync(ReturnCompletedV1 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order => order.MarkReturned(), cancellationToken);

    /// <summary>Snímek ze žádosti o nákupy (objednávky z doby před modulem Reviews).</summary>
    public Task HandleAsync(OrderSnapshotV2 message, CancellationToken cancellationToken) =>
        ChangeAsync(message.OrderId, order =>
        {
            order.ApplyPlaced(message.CustomerId, message.CustomerName, message.Items.Select(i => i.ProductId));
            if (message.Status is "Completed" or "ReturnRequested" or "Returned")
            {
                order.MarkDelivered();
            }

            if (message.Status == "Returned")
            {
                order.MarkReturned();
            }
        }, cancellationToken);

    public async Task HandleAsync(CustomerAnonymizedV1 message, CancellationToken cancellationToken)
    {
        await purchases.RemoveForCustomerAsync(message.CustomerId, cancellationToken);
        foreach (var review in await reviews.GetByCustomerAsync(message.CustomerId, cancellationToken))
        {
            review.AnonymizeAuthor();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ChangeAsync(int orderId, Action<PurchasedOrder> change, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        var order = await purchases.GetAsync(orderId, cancellationToken);
        if (order is null)
        {
            order = PurchasedOrder.Create(orderId);
            purchases.Add(order);
        }

        change(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
