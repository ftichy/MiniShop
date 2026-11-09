using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Payments;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Payments.Stripe;
using MiniShop.Infrastructure.Persistence;
using Stripe;

namespace MiniShop.Infrastructure.Payments;

/// <summary>
/// E-shop krok 8: infrastruktura modulu Payments – payments.db (platby, outbox, inbox), repozitář, simulovaná brána;
/// e-shop krok 16 volitelně Stripe (testovací režim).
/// Vlastní fronta minishop.payments v RabbitMQ a vlastní pošťák (outbox).
/// </summary>
public static class PaymentsInfrastructure
{
    /// <param name="stripe">E-shop krok 16: nastavení Stripu = platby jdou přes Stripe; null = simulovaná brána.</param>
    public static IServiceCollection AddPaymentsInfrastructure(this IServiceCollection services, ModuleDatabase database,
        string? webhookSecret, StripeSettings? stripe = null)
    {
        services.AddDbContext<PaymentsDbContext>(options => options.UseModuleDatabase(database, PaymentsDbContext.Schema));
        services.AddScoped<IPaymentsUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        services.AddSingleton(new PaymentsSettings(webhookSecret));   // fail fast: bez tajemství se služba nespustí
        services.AddScoped<PaymentWebhookProcessor>();
        services.AddScoped<PaymentQueryService>();

        // Simulace brány zůstává vždy (stránka /Pay a testy); kterou bránu dostanou nové platby, určuje IPaymentGateway.
        services.AddScoped<FakePaymentGatewaySimulator>();
        if (stripe is null)
        {
            services.AddSingleton(new PaymentGatewayInfo(PaymentGatewayInfo.Fake));
            services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        }
        else
        {
            // E-shop krok 16: Stripe v testovacím režimu – jen jiná implementace IPaymentGateway (adaptér).
            services.AddSingleton(new PaymentGatewayInfo(PaymentGatewayInfo.Stripe));
            services.AddSingleton(stripe);
            services.AddSingleton<IStripeClient>(_ => new StripeClient(apiKey: stripe.SecretKey));
            services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
            services.AddScoped<StripeWebhookProcessor>();
        }

        services.AddSingleton<IOutboxProcessor, OutboxProcessor<PaymentsDbContext>>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck<PaymentsDbContext>>("outbox-payments");

        services.AddIntegrationModule<PaymentsDbContext>(IntegrationModules.Payments);

        return services;
    }
}

/// <summary>E-shop krok 8: vytvoří nebo aktualizuje payments.db (EF migrace).</summary>
public static class PaymentsDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(), cancellationToken);
    }

    public static Task MigrateAsync(PaymentsDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, cancellationToken);
}
