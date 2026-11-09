using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Invoicing;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Invoicing;

/// <summary>
/// E-shop krok 15: infrastruktura modulu Invoicing – schéma invoicing (kopie objednávek, faktury, číselné řady, inbox,
/// outbox), repozitáře, pošťák a PDF (QuestPDF). Vlastní fronta minishop.invoicing v RabbitMQ.
/// </summary>
public static class InvoicingInfrastructure
{
    public static IServiceCollection AddInvoicingInfrastructure(this IServiceCollection services, ModuleDatabase database)
    {
        services.AddDbContext<InvoicingDbContext>(options => options.UseModuleDatabase(database, InvoicingDbContext.Schema));
        services.AddScoped<IInvoicingUnitOfWork>(sp => sp.GetRequiredService<InvoicingDbContext>());
        services.AddScoped<IInvoicingMessagePublisher, InvoicingMessagePublisher>();
        services.AddScoped<IBillableOrderRepository, BillableOrderRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceNumberSeriesRepository, InvoiceNumberSeriesRepository>();
        services.AddScoped<ICreditNoteRepository, CreditNoteRepository>();                       // e-shop krok 15B
        services.AddScoped<ICreditNoteNumberSeriesRepository, CreditNoteNumberSeriesRepository>();
        services.AddSingleton<IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();

        services.AddSingleton<IOutboxProcessor, OutboxProcessor<InvoicingDbContext>>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck<InvoicingDbContext>>("outbox-invoicing");

        services.AddIntegrationModule<InvoicingDbContext>(IntegrationModules.Invoicing);

        return services;
    }
}

/// <summary>E-shop krok 15: vytvoří nebo aktualizuje schéma invoicing (EF migrace).</summary>
public static class InvoicingDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<InvoicingDbContext>(), cancellationToken);
    }

    public static Task MigrateAsync(InvoicingDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, cancellationToken);
}
