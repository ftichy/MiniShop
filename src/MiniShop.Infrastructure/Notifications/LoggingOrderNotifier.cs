using Microsoft.Extensions.Logging;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Notifications;

/// <summary>
/// Jednoduchá implementace notifikací – jen zapisuje do logu.
/// Lze ji nahradit např. odesíláním e-mailů, aniž by se měnila aplikační vrstva.
/// </summary>
public sealed class LoggingOrderNotifier(ILogger<LoggingOrderNotifier> logger) : IOrderNotifier
{
    public Task OrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Objednávka {OrderId} vytvořena pro {CustomerEmail}, {ItemCount} položek, celkem {Total} Kč",
            order.Id, order.CustomerEmail, order.Items.Count, order.TotalPrice);
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
