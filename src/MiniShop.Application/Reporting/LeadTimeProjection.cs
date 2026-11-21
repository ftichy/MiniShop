using MiniShop.Contracts;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Reporting;

namespace MiniShop.Application.Reporting;

/// <summary>
/// Proud B2: jak zpráva z proudu změní časy objednávky. Vstup jsou jen dvě události z proudu <c>minishop.events</c> –
/// <see cref="OrderStatusChangedV1"/> (každý přechod stavu s časem) a <see cref="ShipmentStatusChangedV1"/> (zásilku veze
/// dopravce). Ostatní zprávy v proudu projekce přeskočí (záložka se posune i přes ně).
/// </summary>
public static class LeadTimeProjection
{
    public static int? OrderIdOf(IIntegrationMessage message) => message switch
    {
        OrderStatusChangedV1 m => m.OrderId,
        ShipmentStatusChangedV1 m => m.OrderId,
        _ => null
    };

    public static void Apply(OrderLeadTime leadTime, IIntegrationMessage message)
    {
        switch (message)
        {
            case OrderStatusChangedV1 changed:
                leadTime.ApplyStatus(changed.Status, changed.OccurredAtUtc);
                break;
            case ShipmentStatusChangedV1:
                leadTime.MarkByCarrier();
                break;
        }
    }
}
