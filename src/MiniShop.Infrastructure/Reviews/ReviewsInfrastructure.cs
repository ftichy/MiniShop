using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application;
using MiniShop.Application.Reviews;
using MiniShop.Infrastructure.Observability;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Reviews;

/// <summary>
/// E-shop krok 14: infrastruktura modulu Reviews – reviews.db (hodnocení, kopie nákupů, inbox, outbox), repozitář, pošťák.
/// Vlastní fronta minishop.reviews v RabbitMQ.
/// </summary>
public static class ReviewsInfrastructure
{
    public static IServiceCollection AddReviewsInfrastructure(this IServiceCollection services, ModuleDatabase database)
    {
        services.AddDbContext<ReviewsDbContext>(options => options.UseModuleDatabase(database, ReviewsDbContext.Schema));
        services.AddScoped<IReviewsUnitOfWork>(sp => sp.GetRequiredService<ReviewsDbContext>());
        services.AddScoped<IReviewsMessagePublisher, ReviewsMessagePublisher>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IPurchaseRepository, PurchaseRepository>();

        services.AddSingleton<IOutboxProcessor, OutboxProcessor<ReviewsDbContext>>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck<ReviewsDbContext>>("outbox-reviews");

        services.AddIntegrationModule<ReviewsDbContext>(IntegrationModules.Reviews);

        return services;
    }
}

/// <summary>E-shop krok 14: vytvoří nebo aktualizuje reviews.db (EF migrace).</summary>
public static class ReviewsDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<ReviewsDbContext>(), cancellationToken);
    }

    public static Task MigrateAsync(ReviewsDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, cancellationToken);
}
