using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Application.Journeys;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Ordering.Checkout;
using MiniShop.Application.Ordering.Coupons;
using MiniShop.Application.Notifications;
using MiniShop.Application.Payments;
using MiniShop.Application.Ordering.IntegrationEvents;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Contracts.Carts;
using MiniShop.Contracts.Customers;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Contracts.Reporting;
using MiniShop.Application.Reporting;
using MiniShop.Application.Invoicing;
using MiniShop.Application.Reviews;
using MiniShop.Contracts.Invoicing;
using MiniShop.Contracts.Reviews;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Ordering;
using MiniShop.ServiceDefaults;
using MiniShop.Web.Accounts;
using MiniShop.Web.Inventory;
using MiniShop.Web.Invoicing;

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
        services.AddPaymentsModule();
        services.AddReportingModule();
        services.AddReviewsModule();
        services.AddInvoicingModule(configuration);
        services.AddInventoryApiClient(configuration);

        return services;
    }

    /// <summary>
    /// E-shop krok 11 (CQRS): modul Reporting – přehled tržeb pro admina jako projekce z událostí, které už existují.
    /// Vlastní fronta minishop.reporting; Ordering ani Payments o něm nevědí. Databázi registruje AddReportingInfrastructure.
    /// </summary>
    public static IServiceCollection AddReviewsModule(this IServiceCollection services)
    {
        // E-shop krok 14 (modul Reviews): hodnocení produktů. Kopie nákupů z existujících událostí objednávek,
        // souhrn hodnocení (ProductRatingChangedV1) jde do kopie katalogu v Orderingu. Databázi registruje AddReviewsInfrastructure.
        services.AddScoped<ReviewService>();
        services.AddIntegrationMessageHandler<OrderPlacedV2, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<OrderCompletedV1, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<OrderSnapshotV2, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        services.AddIntegrationMessageHandler<CustomerAnonymizedV1, PurchaseProjectionHandler>(IntegrationModules.Reviews);
        return services;
    }

    /// <summary>
    /// E-shop krok 15: modul Invoicing – faktury z existujících událostí (vznik objednávky, platba kartou, doručení,
    /// zamítnutí/zrušení). Vystavení oznámí InvoiceIssuedV1 (Notifications pošle e-mail s PDF). Krok 15B: zrušení,
    /// zamítnutí nebo vrácení po vystavení faktury = dobropis (CreditNoteIssuedV1). Databázi a PDF
    /// registruje AddInvoicingInfrastructure.
    /// </summary>
    public static IServiceCollection AddInvoicingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Prodávající na faktuře (fiktivní – výukový projekt); přepsat jde v sekci Invoicing:Seller.
        var seller = InvoiceSeller.Default;
        services.AddSingleton(seller with
        {
            Name = configuration["Invoicing:Seller:Name"] ?? seller.Name,
            Address = configuration["Invoicing:Seller:Address"] ?? seller.Address,
            CompanyId = configuration["Invoicing:Seller:CompanyId"] ?? seller.CompanyId,
            Note = configuration["Invoicing:Seller:Note"] ?? seller.Note
        });
        services.AddScoped<InvoiceService>();
        services.AddIntegrationMessageHandler<OrderPlacedV2, InvoicingHandler>(IntegrationModules.Invoicing);
        services.AddIntegrationMessageHandler<PaymentSucceededV1, InvoicingHandler>(IntegrationModules.Invoicing);
        services.AddIntegrationMessageHandler<OrderCompletedV1, InvoicingHandler>(IntegrationModules.Invoicing);
        services.AddIntegrationMessageHandler<OrderRejectedV1, InvoicingHandler>(IntegrationModules.Invoicing);
        services.AddIntegrationMessageHandler<OrderCancelledV1, InvoicingHandler>(IntegrationModules.Invoicing);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, InvoicingHandler>(IntegrationModules.Invoicing);   // e-shop krok 15B: dobropis
        services.AddIntegrationMessageHandler<CustomerAnonymizedV1, InvoicingHandler>(IntegrationModules.Invoicing);
        return services;
    }

    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<SalesReportService>();
        services.AddIntegrationMessageHandler<OrderPlacedV2, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderConfirmedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderRejectedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderCancelledV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderCompletedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<PaymentRefundedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderSnapshotV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, SalesProjectionHandler>(IntegrationModules.Reporting);   // e-shop krok 12
        return services;
    }

    /// <summary>
    /// E-shop krok 8: modul Payments – účastník ságy objednávky (příkazy RequestPaymentV1, RefundPaymentV1). Databázi,
    /// bránu a tajemství webhooku registruje AddPaymentsInfrastructure.
    /// </summary>
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        services.AddIntegrationMessageHandler<RequestPaymentV1, PaymentCommandHandlers>(IntegrationModules.Payments);
        services.AddIntegrationMessageHandler<RefundPaymentV1, PaymentCommandHandlers>(IntegrationModules.Payments);
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
        services.AddIntegrationMessageHandler<PaymentRefundedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);   // e-shop krok 8
        services.AddIntegrationMessageHandler<ReturnRequestedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);   // e-shop krok 12
        services.AddIntegrationMessageHandler<ReturnRejectedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, OrderNotificationsHandler>(IntegrationModules.Notifications);

        // E-shop krok 10: připomínka opuštěného košíku – kontakt a souhlas až při odeslání (z účtů, přes Web).
        services.AddSingleton<CartEmailTemplates>();
        services.AddScoped<ICustomerContacts, IdentityCustomerContacts>();
        services.AddIntegrationMessageHandler<CartAbandonedV1, CartReminderHandler>(IntegrationModules.Notifications);

        // E-shop krok 15: e-mail s fakturou v příloze – PDF z modulu Invoicing přes most ve Webu.
        services.AddScoped<IInvoiceDocuments, InvoiceDocuments>();
        services.AddIntegrationMessageHandler<InvoiceIssuedV1, InvoiceEmailHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<CreditNoteIssuedV1, InvoiceEmailHandler>(IntegrationModules.Notifications);   // e-shop krok 15B
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
        var paymentMinutes = configuration.GetValue("Sagas:PaymentTimeoutMinutes", 30);   // e-shop krok 8
        services.AddSingleton(new OrderSagaSettings(TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMinutes(paymentMinutes)));
        services.AddDomainEventHandler<OrderPlaced, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderShipped, OrderSagaOrchestrator>();   // e-shop krok 6
        services.AddDomainEventHandler<OrderCompleted, OrderSagaOrchestrator>();
        services.AddDomainEventHandler<OrderCancelled, OrderSagaOrchestrator>();
        services.AddIntegrationMessageHandler<StockReservedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReservationFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockReleasedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        // E-shop krok 8: výsledek platby z modulu Payments (druhý účastník ságy).
        services.AddIntegrationMessageHandler<PaymentSucceededV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<PaymentFailedV1, OrderSagaOrchestrator>(IntegrationModules.Ordering);

        // E-shop krok 12: vrácení zboží – druhá sága (zákazník → admin převezme → sklad naskladní → Payments vrátí peníze).
        services.AddSingleton(new ReturnSettings(
            TimeSpan.FromDays(configuration.GetValue("Returns:WindowDays", 14)),
            TimeSpan.FromDays(configuration.GetValue("Returns:SendWindowDays", 14))));
        services.AddScoped<ReturnSagaOrchestrator>();
        services.AddDomainEventHandler<OrderReturnRequested, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderReturnRejected, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderReturned, PublishOrderIntegrationEventsHandler>();
        services.AddDomainEventHandler<OrderReturnRequested, ReturnSagaOrchestrator>();
        services.AddIntegrationMessageHandler<ReturnedGoodsRestockedV1, ReturnSagaOrchestrator>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<PaymentRefundedV1, ReturnSagaOrchestrator>(IntegrationModules.Ordering);

        // Kopie katalogu (krok 10): produkty ze skladu, bez přístupu do jeho databáze. E-shop krok 2: V2 (popis, kategorie,
        // dostupnost) a změny dostupnosti; V1 zatím zůstává (expand → contract).
        services.AddIntegrationMessageHandler<ProductUpdatedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductUpdatedV2, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<StockAvailabilityChangedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductRemovedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);

        // E-shop krok 11: snímky všech objednávek pro přestavění přehledu tržeb (Reporting si o ně řekne příkazem).
        services.AddIntegrationMessageHandler<RebuildSalesReportV1, RebuildSalesReportHandler>(IntegrationModules.Ordering);
        // E-shop krok 14: snímky nákupů pro modul Reviews a souhrn hodnocení do kopie katalogu (výloha).
        services.AddIntegrationMessageHandler<SyncPurchasesV1, SyncPurchasesHandler>(IntegrationModules.Ordering);
        services.AddIntegrationMessageHandler<ProductRatingChangedV1, CatalogProjectionHandler>(IntegrationModules.Ordering);

        // Obnova kopie katalogu (příkaz SyncCatalogV1 pro sklad): po startu, když je prázdná/neúplná, nebo tlačítkem v UI.
        services.AddScoped<CatalogSyncService>();

        // E-shop krok 2: výloha – hledání nad kopií katalogu.
        services.AddScoped<ICatalogQueryService, CatalogQueryService>();

        // E-shop krok 3: košík (agregát Cart v ordering.db, ceny z kopie katalogu).
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<CartHistoryService>();   // e-shop krok 17: historie a statistika košíků z událostí

        // E-shop krok 10: opuštěný košík – worker (CartReminderWorker) a oznámení CartAbandonedV1.
        services.AddSingleton(new CartReminderSettings(
            TimeSpan.FromHours(configuration.GetValue("Carts:AbandonedAfterHours", 24.0)),
            TimeSpan.FromMinutes(configuration.GetValue("Carts:ReminderCheckMinutes", 15.0))));
        services.AddScoped<CartReminderService>();

        // E-shop krok 4: pokladna (košík → objednávka v jedné transakci) a ceník dopravy (strategie).
        services.AddScoped<ICheckoutService, CheckoutService>();

        // E-shop krok 13: slevové kódy – správa (admin) a vrácení použití po zamítnutí/zrušení objednávky (kompenzace).
        services.AddScoped<CouponService>();
        services.AddDomainEventHandler<OrderRejected, ReleaseCouponHandler>();
        services.AddDomainEventHandler<OrderCancelled, ReleaseCouponHandler>();
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
