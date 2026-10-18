using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Application.Journeys;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Ordering.EventHandlers;
using MiniShop.Application.Ordering.IntegrationEvents;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Contracts.Inventory;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Web.Inventory;

namespace MiniShop.Web.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registrace služeb aplikační vrstvy. Od kroku 10B hostuje Web jen modul Ordering –
    /// sklad běží jako samostatná služba MiniShop.Inventory.Api.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOrderingModule(configuration);
        services.AddJourneysModule();
        services.AddInventoryApiClient();

        return services;
    }

    /// <summary>Krok 12: modul Journeys – co uživatel dělá ve formuláři objednávky (proud událostí + projekce).</summary>
    public static IServiceCollection AddJourneysModule(this IServiceCollection services)
    {
        services.AddScoped<IJourneyService, JourneyService>();
        return services;
    }

    /// <summary>Modul Ordering: objednávky, sága, kopie katalogu, notifikace.</summary>
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOrderService, OrderService>();

        // Slevová pravidla – další stačí přidat sem (OrderService si vezme všechna).
        services.AddSingleton<IDiscountPolicy>(new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m));

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

        // Sága objednávky (krok 9, orchestrace): orchestrátor poslouchá události objednávky (doménové)
        // a odpovědi skladu (integrační).
        var timeoutSeconds = configuration.GetValue("Sagas:ReservationTimeoutSeconds", 30);
        services.AddSingleton(new OrderSagaSettings(TimeSpan.FromSeconds(timeoutSeconds)));
        services.AddDomainEventHandler<OrderPlaced, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCompleted, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCancelled, OrderSagaOrchestrator>();
        services.AddIntegrationMessageHandler<StockReservedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReservationFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReleasedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);

        // Kopie katalogu (krok 10): název a cena produktů ze skladu, bez přístupu do jeho databáze.
        services.AddIntegrationMessageHandler<ProductUpdatedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductRemovedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);

        // Obnova kopie katalogu (příkaz SyncCatalogV1 pro sklad): po startu, když je prázdná, nebo tlačítkem v UI.
        services.AddScoped<CatalogSyncService>();

        return services;
    }

    /// <summary>
    /// Krok 10B: stránky Produkty a Objednávky čtou sklad přes jeho HTTP API. "inventory-api" je jméno služby
    /// v AppHostu Aspire; skutečnou adresu doplní service discovery (bez Aspire ze sekce "Services" v appsettings.json).
    /// </summary>
    public static IServiceCollection AddInventoryApiClient(this IServiceCollection services)
    {
        services.AddHttpClient<IProductService, InventoryApiClient>(http =>
            http.BaseAddress = new Uri("https+http://inventory-api"));

        return services;
    }
}
