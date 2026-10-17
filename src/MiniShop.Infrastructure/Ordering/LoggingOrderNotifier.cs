using Microsoft.Extensions.Logging;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// Jednoduchá implementace notifikací – jen zapisuje do logu.
/// Lze ji nahradit např. odesíláním e-mailů, aniž by se měnila aplikační vrstva.
/// </summary>
public sealed class LoggingOrderNotifier(ILogger<LoggingOrderNotifier> logger) : IOrderNotifier
{
    public Task OrderConfirmedAsync(Order order, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Objednávka {OrderId} potvrzena pro {CustomerEmail}, {ItemCount} položek, celkem {Total} Kč",
            order.Id, order.CustomerEmail, order.Items.Count, order.TotalPrice);
        return Task.CompletedTask;
    }

    public Task OrderRejectedAsync(Order order, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Omlouváme se, objednávku {OrderId} pro {CustomerEmail} nelze vyřídit: {Reason}",
            order.Id, order.CustomerEmail, order.RejectionReason);
        return Task.CompletedTask;
    }

    public Task OrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Objednávka {OrderId} byla zrušena", order.Id);
        return Task.CompletedTask;
    }

    public Task OrderCompletedAsync(Order order, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Objednávka {OrderId} byla vyřízena", order.Id);
        return Task.CompletedTask;
    }
}
