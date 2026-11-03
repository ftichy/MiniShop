using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Reporting;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Reporting;

/// <summary>
/// E-shop krok 11: infrastruktura modulu Reporting – reporting.db (čtecí model, inbox, outbox), repozitář, pošťák.
/// Vlastní fronta minishop.reporting v RabbitMQ.
/// </summary>
public static class ReportingInfrastructure
{
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ReportingDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IReportingUnitOfWork>(sp => sp.GetRequiredService<ReportingDbContext>());
        services.AddScoped<IReportingMessagePublisher, ReportingMessagePublisher>();
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();

        services.AddSingleton<IOutboxProcessor, OutboxProcessor<ReportingDbContext>>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck<ReportingDbContext>>("outbox-reporting");

        services.AddIntegrationModule<ReportingDbContext>(IntegrationModules.Reporting);

        return services;
    }
}

/// <summary>E-shop krok 11: vytvoří nebo aktualizuje reporting.db (EF migrace).</summary>
public static class ReportingDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<ReportingDbContext>(), cancellationToken);
    }

    public static Task MigrateAsync(ReportingDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, "SalesOrders", cancellationToken);
}

/// <summary>
/// E-shop krok 11: jen pro nástroj <c>dotnet ef</c>. Nová migrace (z kořene repozitáře):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure --context ReportingDbContext --output-dir Reporting/Migrations</code>
/// </summary>
internal sealed class ReportingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ReportingDbContext>().UseSqlite("Data Source=reporting.db").Options);
}
