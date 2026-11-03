using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Application.Journeys;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Ordering.Checkout;
using MiniShop.Application.Notifications;
using MiniShop.Application.Ordering.IntegrationEvents;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Contracts.Customers;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Ordering;
using MiniShop.ServiceDefaults;
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
        services.AddNotificationsModule(configuration);
        services.AddInventoryApiClient(configuration);

        return services;
    }

    /// <summary>
    /// E-shop krok 7: modul Notifications – e-maily zákazníkovi podle integračních událostí objednávky (pub/sub).
    /// Vlastní fronta minishop.notifications; Ordering o něm neví. Příjemce si pamatuje z OrderPlacedV2.
    /// </summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Odkazy v e-mailech (/Orders/{id}) potřebují veřejnou adresu webu – handler běží bez HTTP požadavku.
        services.AddSingleton(new NotificationSettings(configuration["Notifications:PublicBaseUrl"]));
        services.AddSingleton<OrderEmailTemplates>();

        services.AddIntegrationMessageHandler<OrderPlacedV2, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderConfirmedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderRejectedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderShippedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderCompletedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderCancelledV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<CustomerAnonymizedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        return services;
    }

    /// <summary>Krok 12: modul Journeys – co uživatel dělá ve formuláři objednávky (proud událostí + projekce).</summary>
    public static IServiceCollection AddJourneysModule(this IServiceCollection services)
    {
        services.AddScoped<IJourneyService, JourneyService>();
        return services;
    }

    /// <summary>Modul Ordering: objednávky, sága, kopie katalogu, košík, pokladna, expedice.</summary>
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOrderService, OrderService>();

        // Slevová pravidla – další stačí přidat sem (OrderService si vezme všechna).
        services.AddSingleton<IDiscountPolicy>(new QuantityDiscountPolicy(minimumQuantity: 10, percentage: 0.10m));

        // Handlery doménových událostí (krok 3) obalí AddDomainEventHandler dekorátorem proti opakovanému doručení (krok 6).
        // E-shop krok 7: e-maily zákazníkovi už neposílá Ordering (Send…Handler zmizely) – jen překladač níže oznámí
        // fakta a modul Notifications si podle nich píše sám.

        // Překlad doménových událostí na integrační (krok 7) – jedna třída pro všechny události objednávky.
        services.AddDomainEventHandler<OrderPlaced, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderConfirmed, PublishOrderIntegrationEventsHandler>();   // e-shop krok 7
        services.AddDomainEventHandler<OrderRejected, PublishOrderIntegrationEventsHandler>();    // e-shop krok 7
        services.AddDomainEventHandler<OrderShipped, PublishOrderIntegrationEventsHandler>();   // e-shop krok 6
        services.AddDomainEventHandler<OrderCompleted, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderCancelled, PublishOrderIntegrationEventsHandler>();

        // Sága objednávky (krok 9, orchestrace): orchestrátor poslouchá události objednávky (doménové)
        // a odpovědi skladu (integrační).
        var timeoutSeconds = configuration.GetValue("Sagas:ReservationTimeoutSeconds", 30);
        services.AddSingleton(new OrderSagaSettings(TimeSpan.FromSeconds(timeoutSeconds)));
        services.AddDomainEventHandler<OrderPlaced, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderShipped, OrderSagaOrchestrator>();   // e-shop krok 6
        services.AddDomainEventHandler<OrderCompleted, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCancelled, OrderSagaOrchestrator>();
        services.AddIntegrationMessageHandler<StockReservedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReservationFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReleasedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);

        // Kopie katalogu (krok 10): produkty ze skladu, bez přístupu do jeho databáze. E-shop krok 2: V2 (popis, kategorie,
        // dostupnost) a změny dostupnosti; V1 zatím zůstává (expand → contract).
        services.AddIntegrationMessageHandler<ProductUpdatedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductUpdatedV2, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockAvailabilityChangedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductRemovedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);

        // Obnova kopie katalogu (příkaz SyncCatalogV1 pro sklad): po startu, když je prázdná/neúplná, nebo tlačítkem v UI.
        services.AddScoped<CatalogSyncService>();

        // E-shop krok 2: výloha – hledání nad kopií katalogu.
        services.AddScoped<ICatalogQueryService, CatalogQueryService>();

        // E-shop krok 3: košík (agregát Cart v ordering.db, ceny z kopie katalogu).
        services.AddScoped<ICartService, CartService>();

        // E-shop krok 4: pokladna (košík → objednávka v jedné transakci) a ceník dopravy (strategie).
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddSingleton<IShippingPriceCalculator>(new StandardShippingPriceCalculator(courierPrice: 99m, freeFrom: 1_000m));

        return services;
    }

    /// <summary>
    /// Krok 10B: stránky Produkty a Objednávky čtou sklad přes jeho HTTP API. "inventory-api" je jméno služby
    /// v AppHostu Aspire; skutečnou adresu doplní service discovery (bez Aspire ze sekce "Services" v appsettings.json).
    /// Účty krok 7: každý požadavek nese servisní klíč skladu (<see cref="InventoryApiKeyHandler"/>); bez klíče
    /// v konfiguraci se Web nespustí.
    /// </summary>
    public static IServiceCollection AddInventoryApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = ServiceApiKey.Read(key => configuration[key]);

        services.AddHttpClient<InventoryApiClient>(http =>
                http.BaseAddress = new Uri("https+http://inventory-api"))
            .AddHttpMessageHandler(() => new InventoryApiKeyHandler(apiKey));

        // Jeden klient pro produkty i kategorie (e-shop krok 2) – stránky dostávají jen rozhraní, které potřebují (ISP).
        services.AddTransient<IProductService>(sp => sp.GetRequiredService<InventoryApiClient>());
        services.AddTransient<ICategoryService>(sp => sp.GetRequiredService<InventoryApiClient>());
        services.AddTransient<IProductImageService>(sp => sp.GetRequiredService<InventoryApiClient>());   // e-shop krok 2B

        return services;
    }
}
