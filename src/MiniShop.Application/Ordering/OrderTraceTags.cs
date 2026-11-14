using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Application.Ordering;

/// <summary>
/// Business údaje objednávky na trasu (Grafana → vodopád objednávky ukáže „co“, nejen „kdy“). Bez osobních údajů –
/// adresa, jméno ani e-mail sem nepatří (viz <see cref="TraceTags.Tag"/>).
/// </summary>
public static class OrderTraceTags
{
    /// <summary>Vznik objednávky (pokladna, API): platba, doprava, počet řádků a kusů, celkem, slevový kód.</summary>
    public static void TagPlaced(Order order)
    {
        TraceTags.TagOrder(order.Id);
        TraceTags.Tag("minishop.order.payment", order.PaymentMethod.ToString());
        TraceTags.Tag("minishop.order.shipping", order.ShippingMethod.ToString());
        TraceTags.Tag("minishop.order.items", order.Items.Count);
        TraceTags.Tag("minishop.order.quantity", order.Items.Sum(item => item.Quantity));
        TraceTags.Tag("minishop.order.total", (double)order.TotalPrice);
        TraceTags.Tag("minishop.order.lines", TraceTags.Lines(order.Items.Select(item => (item.ProductId, item.Quantity))));
        TraceTags.Tag("minishop.order.coupon", order.CouponCode);
    }

    /// <summary>Krok ságy: stav před a po, co orchestrátor udělal, výsledný stav objednávky (u zamítnutí důvod).</summary>
    public static void TagSagaStep(OrderSagaState before, OrderSaga saga, OrderSagaAction action, Order order)
    {
        TraceTags.Tag("minishop.saga.state_before", before.ToString());
        TraceTags.Tag("minishop.saga.state", saga.State.ToString());
        TraceTags.Tag("minishop.saga.action", action.ToString());
        TraceTags.Tag("minishop.order.status", order.Status.ToString());
        if (order.Status == OrderStatus.Rejected)
        {
            TraceTags.Tag("minishop.order.rejection_reason", order.RejectionReason);
        }
    }

    /// <summary>Změna stavu objednávky mimo ságu (expedice, doručení, zrušení, vratka).</summary>
    public static void TagStatus(Order order) => TraceTags.Tag("minishop.order.status", order.Status.ToString());

    /// <summary>Akce v košíku (add / update / remove / clear / checkout) – ID produktu a kusy, ne název.</summary>
    public static void TagCart(string action, int? productId = null, int? quantity = null)
    {
        TraceTags.Tag("minishop.cart.action", action);
        if (productId is not null)
        {
            TraceTags.Tag("minishop.product.id", productId);
        }

        if (quantity is not null)
        {
            TraceTags.Tag("minishop.cart.quantity", quantity);
        }
    }
}
