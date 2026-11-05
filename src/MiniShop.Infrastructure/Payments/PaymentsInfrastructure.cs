using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Payments;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Payments;

/// <summary>
/// E-shop krok 8: infrastruktura modulu Payments – payments.db (platby, outbox, inbox), repozitář, simulovaná brána.
/// Vlastní fronta minishop.payments v RabbitMQ a vlastní pošťák (outbox).
/// </summary>
public static class PaymentsInfrastructure
{
    public static IServiceCollection AddPaymentsInfrastructure(this IServiceCollection services, ModuleDatabase database,
        string? webhookSecret)
    {
        services.AddDbContext<PaymentsDbContext>(options => options.UseModuleDatabase(database, PaymentsDbContext.Schema));
        services.AddScoped<IPaymentsUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        services.AddSingleton(new PaymentsSettings(webhookSecret));   // fail fast: bez tajemství se služba nespustí
        services.AddScoped<PaymentWebhookProcessor>();
        services.AddScoped<PaymentQueryService>();

        // Simulace brány (skutečná = jiná implementace IPaymentGateway).
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.AddScoped<FakePaymentGatewaySimulator>();

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
