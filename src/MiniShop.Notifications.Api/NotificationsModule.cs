using MiniShop.Application;
using MiniShop.Application.Notifications;
using MiniShop.Contracts.Carts;
using MiniShop.Contracts.Customers;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Invoicing;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Notifications;
using MiniShop.ServiceDefaults;

namespace MiniShop.Notifications.Api;

/// <summary>
/// E-shop krok 7: modul Notifications – e-maily a oznámení push podle integračních událostí (pub/sub). Vlastní fronta
/// minishop.notifications; Ordering o něm neví. Mikroservisy M2C: registrace se přestěhovala z Webu sem.
/// </summary>
public static class NotificationsModule
{
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
        services.AddIntegrationMessageHandler<ReturnRequestedV2, OrderNotificationsHandler>(IntegrationModules.Notifications);   // e-shop krok 22: vratka po částech
        services.AddIntegrationMessageHandler<ReturnRejectedV2, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnCompletedV2, OrderNotificationsHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderReturnedToSenderV1, OrderNotificationsHandler>(IntegrationModules.Notifications);   // e-shop krok 26D

        // E-shop krok 10: připomínka opuštěného košíku – kontakt a souhlas až při odeslání (z účtů ve Webu, M2A interní API).
        services.AddSingleton<CartEmailTemplates>();
        services.AddIntegrationMessageHandler<CartAbandonedV1, CartReminderHandler>(IntegrationModules.Notifications);

        // E-shop krok 18: „Dejte mi vědět“ – lísteček u vyprodaného zboží, jeden e-mail po naskladnění.
        services.AddSingleton<BackInStockEmailTemplates>();
        services.AddScoped<BackInStockService>();
        services.AddIntegrationMessageHandler<StockAvailabilityChangedV1, BackInStockHandler>(IntegrationModules.Notifications);

        // E-shop krok 23: oznámení push – druhý kanál vedle e-mailu (vlastní handler = vlastní inbox, chyba push služby
        // nezopakuje e-mail). Bez klíčů VAPID se neposílají.
        var push = PushSettings.FromValues(configuration["PushNotifications:VapidPublicKey"],
            configuration["PushNotifications:VapidPrivateKey"], configuration["Notifications:PublicBaseUrl"]);
        if (push.IsConfigured)
        {
            services.AddSingleton(push);
            services.AddHttpClient<IPushSender, WebPushSender>();
        }
        else
        {
            services.AddSingleton<IPushSender, DisabledPushSender>();
        }

        services.AddScoped<PushNotifier>();
        services.AddScoped<PushSubscriptionService>();
        services.AddOrderPushHandlers();

        // E-shop krok 24: oznámení k práci pro zaměstnance.
        services.AddScoped<StaffAlertService>();
        services.AddStaffPushHandlers();

        // E-shop krok 15: e-mail s fakturou v příloze (PDF z interního API Webu).
        services.AddIntegrationMessageHandler<InvoiceIssuedV1, InvoiceEmailHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<CreditNoteIssuedV1, InvoiceEmailHandler>(IntegrationModules.Notifications);   // e-shop krok 15B
        return services;
    }

    /// <summary>
    /// Mikroservisy M2A/M2C: kontakt zákazníka, role zaměstnanců, název produktu a PDF faktur – z interního API Webu
    /// (<c>https+http://web</c>, klíč <c>WebInternalApi:ApiKey</c>). Bez klíče se služba nespustí.
    /// </summary>
    public static IServiceCollection AddWebInternalApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = ServiceApiKey.Read(key => configuration[key], ServiceApiKey.WebInternalConfigurationKey);
        services.AddHttpClient<WebInternalApiClient>(http => http.BaseAddress = new Uri("https+http://web"))
            .AddHttpMessageHandler(() => new ServiceApiKeyHandler(apiKey));
        services.AddTransient<ICustomerContacts>(sp => sp.GetRequiredService<WebInternalApiClient>());
        services.AddTransient<IStaffDirectory>(sp => sp.GetRequiredService<WebInternalApiClient>());
        services.AddTransient<IProductNames>(sp => sp.GetRequiredService<WebInternalApiClient>());
        services.AddTransient<IInvoiceDocuments>(sp => sp.GetRequiredService<WebInternalApiClient>());
        return services;
    }

    /// <summary>E-shop krok 23: události objednávky, o kterých jde oznámení push (sdílí i testy).</summary>
    public static IServiceCollection AddOrderPushHandlers(this IServiceCollection services)
    {
        services.AddIntegrationMessageHandler<OrderConfirmedV1, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderRejectedV1, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderShippedV1, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderCompletedV1, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<OrderCancelledV1, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnRejectedV1, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnRejectedV2, OrderPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnCompletedV2, OrderPushHandler>(IntegrationModules.Notifications);
        return services;
    }

    /// <summary>E-shop krok 24: události, o kterých dostávají oznámení zaměstnanci (sdílí i testy).</summary>
    public static IServiceCollection AddStaffPushHandlers(this IServiceCollection services)
    {
        services.AddIntegrationMessageHandler<OrderConfirmedV1, StaffPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnRequestedV1, StaffPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<ReturnRequestedV2, StaffPushHandler>(IntegrationModules.Notifications);
        services.AddIntegrationMessageHandler<StockAvailabilityChangedV1, StaffPushHandler>(IntegrationModules.Notifications);
        return services;
    }
}
