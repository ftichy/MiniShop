using MiniShop.Application.Orders.EventHandlers;
using MiniShop.Application.Orders.IntegrationEvents;
using MiniShop.Application.Pricing;
using MiniShop.Application.Services;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure;

namespace MiniShop.Web.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registrace služeb aplikační vrstvy.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();

        // Slevová pravidla – další stačí přidat sem (OrderService si vezme všechna).
        services.AddSingleton<IDiscountPolicy>(new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m));

        services.AddSingleton(TimeProvider.System);

        // Handlery doménových událostí (krok 3). Na jednu událost může poslouchat více handlerů.
        // AddDomainEventHandler je obalí dekorátorem, který hlídá opakované doručení (krok 6).
        services.AddDomainEventHandler<OrderPlaced, SendOrderConfirmationHandler>();
        services.AddDomainEventHandler<OrderCompleted, SendCompletionNoticeHandler>();
        services.AddDomainEventHandler<OrderCancelled, ReturnStockOnOrderCancelledHandler>();
        services.AddDomainEventHandler<OrderCancelled, SendCancellationNoticeHandler>();

        // Překlad doménových událostí na integrační (krok 7) – jedna třída pro všechny události objednávky.
        services.AddDomainEventHandler<OrderPlaced, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCompleted, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCancelled, PublishOrderIntegrationEventsHandler>();

        return services;
    }
}
