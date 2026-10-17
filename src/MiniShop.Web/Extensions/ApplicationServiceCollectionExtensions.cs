using MiniShop.Application.Abstractions;
using MiniShop.Application.Orders.EventHandlers;
using MiniShop.Application.Pricing;
using MiniShop.Application.Services;
using MiniShop.Domain.Orders.Events;

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
        services.AddScoped<IDomainEventHandler<OrderPlaced>, SendOrderConfirmationHandler>();
        services.AddScoped<IDomainEventHandler<OrderCompleted>, SendCompletionNoticeHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelled>, ReturnStockOnOrderCancelledHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelled>, SendCancellationNoticeHandler>();

        return services;
    }
}
