using MiniShop.Application;
using MiniShop.Application.Reporting;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Contracts.Reporting;
using MiniShop.Infrastructure;

namespace MiniShop.Reporting.Api;

/// <summary>
/// E-shop krok 11 (CQRS): modul Reporting – přehled tržeb jako projekce z událostí, které už existují. Vlastní fronta
/// minishop.reporting; Ordering ani Payments o něm nevědí. Mikroservisy M1: registrace se přestěhovala z Webu sem.
/// Databázi registruje AddReportingInfrastructure.
/// </summary>
public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<SalesReportService>();
        services.AddScoped<LeadTimeReportService>();   // proud B2: Rychlost vyřízení (plní LeadTimeProjectionWorker z proudu)
        services.AddScoped<ProductRecommendationService>();   // e-shop krok 27A: „Zákazníci také koupili“ (dávka ProductRecommendationsWorker)
        services.AddIntegrationMessageHandler<OrderPlacedV2, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderConfirmedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderRejectedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderCancelledV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderCompletedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<PaymentRefundedV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderSnapshotV1, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<ReturnCompletedV1, SalesProjectionHandler>(IntegrationModules.Reporting);   // e-shop krok 12
        services.AddIntegrationMessageHandler<ReturnCompletedV2, SalesProjectionHandler>(IntegrationModules.Reporting);   // e-shop krok 22: vratka po částech
        services.AddIntegrationMessageHandler<PaymentRefundedV2, SalesProjectionHandler>(IntegrationModules.Reporting);
        services.AddIntegrationMessageHandler<OrderPartialReturnsSnapshotV1, SalesProjectionHandler>(IntegrationModules.Reporting);   // přestavění
        services.AddIntegrationMessageHandler<OrderReturnedToSenderV1, SalesProjectionHandler>(IntegrationModules.Reporting);   // e-shop krok 26D
        return services;
    }
}
