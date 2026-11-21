using Microsoft.AspNetCore.SignalR;
using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Contracts.Shipping;
using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Web.LiveUpdates;

/// <summary>
/// Vzhled krok F3a: zpráva rozeslaná <b>každé kopii</b> Webu (<see cref="RabbitMqBroadcastConsumer"/>) → signál
/// <c>changed(téma)</c> prohlížečům připojeným k <b>této</b> kopii. Kdo je připojený k druhé kopii, dostane ho od ní –
/// proto rozeslání, ne běžná fronta (ta by zprávu dala jen jedné kopii).
/// </summary>
/// <remarks>Signál nenese žádná data – stránka si výřez stáhne sama, se svými oprávněními.</remarks>
public sealed class LiveUpdatesNotifier(IHubContext<LiveUpdatesHub> hub) : IBroadcastMessageHandler
{
    /// <summary>Které zprávy rozesílat. Nový stav na stránce, který nemá vlastní událost = doplnit sem (a do <see cref="Targets"/>).</summary>
    public static readonly Type[] Contracts =
    [
        typeof(OrderStatusChangedV1),         // každá změna stavu objednávky
        typeof(ShipmentFailedV1),             // dopravce odmítl – hláška v Expedici a na detailu
        typeof(ReturnRequestedV2), typeof(ReturnRejectedV2), typeof(ReturnCompletedV2),   // vratky po částech (stav objednávky se nemění)
        typeof(ReturnedGoodsRestockedV1), typeof(ReturnedGoodsRestockedV2),               // sága vratky: naskladněno
        typeof(PaymentRefundedV1), typeof(PaymentRefundedV2)                              // sága vratky: peníze vráceny
    ];

    public async Task HandleAsync(IIntegrationMessage message, CancellationToken cancellationToken)
    {
        foreach (var (group, topic) in Targets(message))
        {
            await hub.Clients.Group(group).SendAsync(LiveUpdatesHub.ChangedMethod, topic, cancellationToken);
        }
    }

    /// <summary>Komu (skupina) a s jakým tématem poslat signál. Veřejné kvůli testům.</summary>
    public static IReadOnlyList<(string Group, string Topic)> Targets(IIntegrationMessage message)
    {
        var orderId = message switch
        {
            OrderStatusChangedV1 m => m.OrderId,
            ShipmentFailedV1 m => m.OrderId,
            ReturnRequestedV2 m => m.OrderId,
            ReturnRejectedV2 m => m.OrderId,
            ReturnCompletedV2 m => m.OrderId,
            ReturnedGoodsRestockedV1 m => m.OrderId,
            ReturnedGoodsRestockedV2 m => m.OrderId,
            PaymentRefundedV1 m => m.OrderId,
            PaymentRefundedV2 m => m.OrderId,
            _ => 0
        };
        if (orderId == 0)
        {
            return [];
        }

        TraceTags.TagOrder(orderId);
        var order = LiveTopics.Order(orderId);
        var targets = new List<(string, string)> { (LiveTopics.OrderGroup(orderId), order) };

        if (message is OrderStatusChangedV1 changed)
        {
            if (!string.IsNullOrEmpty(changed.CustomerId))
            {
                targets.Add((LiveTopics.CustomerGroup(changed.CustomerId), LiveTopics.MyOrders));
            }

            targets.Add((LiveTopics.AllOrders, LiveTopics.AllOrders));
            targets.Add((LiveTopics.Fulfilment, LiveTopics.Fulfilment));
            targets.Add((LiveTopics.Returns, LiveTopics.Returns));
        }
        else if (message is ShipmentFailedV1)
        {
            targets.Add((LiveTopics.Fulfilment, LiveTopics.Fulfilment));
        }
        else
        {
            targets.Add((LiveTopics.Returns, LiveTopics.Returns));   // vratky a jejich sága
        }

        return targets;
    }
}
