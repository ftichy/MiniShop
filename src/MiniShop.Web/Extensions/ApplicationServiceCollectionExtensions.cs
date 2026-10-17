using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Application.Orders.EventHandlers;
using MiniShop.Application.Orders.IntegrationEvents;
using MiniShop.Application.Orders.Saga;
using MiniShop.Application.Pricing;
using MiniShop.Application.Services;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure;

namespace MiniShop.Web.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registrace služeb aplikační vrstvy.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();

        // Slevová pravidla – další stačí přidat sem (OrderService si vezme všechna).
        services.AddSingleton<IDiscountPolicy>(new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m));

        services.AddSingleton(TimeProvider.System);

        // Handlery doménových událostí (krok 3). Na jednu událost může poslouchat více handlerů.
        // AddDomainEventHandler je obalí dekorátorem, který hlídá opakované doručení (krok 6).
        services.AddDomainEventHandler<OrderConfirmed, SendOrderConfirmationHandler>();
        services.AddDomainEventHandler<OrderRejected, SendRejectionNoticeHandler>();
        services.AddDomainEventHandler<OrderCompleted, SendCompletionNoticeHandler>();
        services.AddDomainEventHandler<OrderCancelled, SendCancellationNoticeHandler>();

        // Překlad doménových událostí na integrační (krok 7) – jedna třída pro všechny události objednávky.
        services.AddDomainEventHandler<OrderPlaced, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCompleted, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCancelled, PublishOrderIntegrationEventsHandler>();

        services.AddOrderSaga(configuration);
        services.AddInventoryModule();

        return services;
    }

    /// <summary>
    /// Sága objednávky (krok 9, orchestrace): orchestrátor poslouchá události objednávky (doménové)
    /// a odpovědi skladu (integrační, modul Ordering).
    /// </summary>
    public static IServiceCollection AddOrderSaga(this IServiceCollection services, IConfiguration configuration)
    {
        var timeoutSeconds = configuration.GetValue("Sagas:ReservationTimeoutSeconds", 30);
        services.AddSingleton(new OrderSagaSettings(TimeSpan.FromSeconds(timeoutSeconds)));

        services.AddDomainEventHandler<OrderPlaced, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCompleted, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCancelled, OrderSagaOrchestrator>();
        services.AddIntegrationMessageHandler<StockReservedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReservationFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReleasedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);

        return services;
    }

    /// <summary>Modul Inventory (krok 9): vyřizuje příkazy skladu. V kroku 10 se z něj stane samostatná služba.</summary>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddIntegrationMessageHandler<ReserveStockV1, ReserveStockHandler>(IntegrationModules.Inventory);
        services.AddIntegrationMessageHandler<ReleaseStockV1, ReleaseStockHandler>(IntegrationModules.Inventory);

        return services;
    }
}
